#include <ArduinoJson.h>
#include <HTTPClient.h>
#include <MFRC522.h>
#include <Preferences.h>
#include <SPI.h>
#include <WiFi.h>
#include <WiFiClient.h>
#include <esp_system.h>

// Sesuaikan nilai ini dengan jaringan, perangkat, dan vehicle_type_id database.
constexpr char WIFI_SSID[] = "PARKIR-LAN";
constexpr char WIFI_PASSWORD[] = "ganti-password";
constexpr char API_URL[] = "http://192.168.1.10:5080/api/entries";
constexpr char API_MEDIA_URL[] = "http://192.168.1.10:5080/api/media/entries";
constexpr char API_HEARTBEAT_URL[] = "http://192.168.1.10:5080/api/devices/heartbeat";
constexpr char CAMERA_SNAPSHOT_URL[] = "http://192.168.1.60/snapshot.jpg";
constexpr char DEFAULT_GATE_ID[] = "GATE-IN-01";
constexpr char DEVICE_NAME[] = "Gate Masuk 01";
constexpr char FIRMWARE_VERSION[] = "1.0.0";
constexpr char PRINTER_HOST[] = "192.168.1.50";
constexpr uint16_t PRINTER_PORT = 9100;
constexpr uint32_t VEHICLE_TYPE_ID = 1;

constexpr uint8_t BUTTON_PIN = 27;
constexpr uint8_t LOOP_SENSOR_PIN = 25;
constexpr uint8_t GATE_RELAY_PIN = 26;
constexpr uint8_t RFID_SS_PIN = 5;
constexpr uint8_t RFID_RST_PIN = 4;
constexpr uint32_t BUTTON_DEBOUNCE_MS = 40;
constexpr uint32_t GATE_MIN_OPEN_MS = 800;
constexpr uint32_t GATE_MAX_OPEN_MS = 15000;
constexpr uint32_t HEARTBEAT_INTERVAL_MS = 20000;

MFRC522 rfid(RFID_SS_PIN, RFID_RST_PIN);
Preferences preferences;
String gateId;
String deviceApiKey;
String pendingEntryKey;

bool gateIsOpen = false;
bool vehicleCleared = false;
bool lastButtonReading = HIGH;
bool stableButtonState = HIGH;
uint32_t lastButtonChangeAt = 0;
uint32_t gateOpenedAt = 0;
uint32_t lastHeartbeatAt = 0;
uint32_t lastWifiAttemptAt = 0;
uint32_t wifiRetryDelayMs = 1000;

void addDeviceHeaders(HTTPClient &http) {
  http.addHeader("X-Device-Id", gateId);
  if (deviceApiKey.length() > 0) {
    http.addHeader("X-API-Key", deviceApiKey);
  }
}

void ensureWifi() {
  if (WiFi.status() == WL_CONNECTED) {
    wifiRetryDelayMs = 1000;
    return;
  }

  if (millis() - lastWifiAttemptAt < wifiRetryDelayMs) {
    return;
  }

  lastWifiAttemptAt = millis();
  Serial.printf("Wi-Fi terputus; mencoba reconnect (backoff %lu ms).\n", (unsigned long)wifiRetryDelayMs);
  WiFi.disconnect();
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  wifiRetryDelayMs = wifiRetryDelayMs < 30000 ? wifiRetryDelayMs * 2 : 30000;
}

bool reportPrintStatus(const String &ticketId, const char *status, const String &detail) {
  if (WiFi.status() != WL_CONNECTED || deviceApiKey.length() == 0) {
    return false;
  }

  JsonDocument requestBody;
  requestBody["status"] = status;
  if (detail.length() > 0) {
    requestBody["detail"] = detail;
  }
  String body;
  serializeJson(requestBody, body);

  HTTPClient http;
  http.setConnectTimeout(750);
  http.setTimeout(1000);
  if (!http.begin(String(API_URL) + "/" + ticketId + "/print-status")) {
    return false;
  }
  http.addHeader("Content-Type", "application/json");
  addDeviceHeaders(http);
  const int statusCode = http.POST(body);
  http.end();
  if (statusCode != HTTP_CODE_NO_CONTENT) {
    Serial.printf("Laporan status cetak gagal, HTTP %d\n", statusCode);
    return false;
  }
  return true;
}

bool vehiclePresent() {
  return digitalRead(LOOP_SENSOR_PIN) == LOW;
}

bool buttonPressed() {
  const bool reading = digitalRead(BUTTON_PIN);
  if (reading != lastButtonReading) {
    lastButtonReading = reading;
    lastButtonChangeAt = millis();
  }

  if (millis() - lastButtonChangeAt >= BUTTON_DEBOUNCE_MS && reading != stableButtonState) {
    stableButtonState = reading;
    return stableButtonState == LOW;
  }
  return false;
}

String readRfidUid() {
  if (!rfid.PICC_IsNewCardPresent() || !rfid.PICC_ReadCardSerial()) {
    return "";
  }

  String uid;
  for (byte index = 0; index < rfid.uid.size; index++) {
    if (rfid.uid.uidByte[index] < 0x10) {
      uid += '0';
    }
    uid += String(rfid.uid.uidByte[index], HEX);
  }
  uid.toUpperCase();
  rfid.PICC_HaltA();
  rfid.PCD_StopCrypto1();
  return uid;
}

bool capturePhoto(String &photoPath) {
  if (strlen(CAMERA_SNAPSHOT_URL) == 0) {
    Serial.println("URL snapshot kamera belum dikonfigurasi.");
    return false;
  }

  HTTPClient camera;
  camera.setConnectTimeout(1000);
  camera.setTimeout(2000);
  const char *responseHeaders[] = {"Content-Type"};
  camera.collectHeaders(responseHeaders, 1);
  if (!camera.begin(CAMERA_SNAPSHOT_URL)) {
    return false;
  }

  const int captureStatus = camera.GET();
  const int imageLength = camera.getSize();
  if (captureStatus != HTTP_CODE_OK || imageLength <= 0 || imageLength > 5 * 1024 * 1024) {
    Serial.printf("Capture kamera gagal, HTTP %d, bytes %d\n", captureStatus, imageLength);
    camera.end();
    return false;
  }

  String contentType = camera.header("Content-Type");
  const int contentTypeParameters = contentType.indexOf(';');
  if (contentTypeParameters >= 0) {
    contentType = contentType.substring(0, contentTypeParameters);
  }

  WiFiClient *imageStream = camera.getStreamPtr();
  HTTPClient upload;
  upload.setConnectTimeout(1000);
  upload.setTimeout(2500);
  if (!upload.begin(API_MEDIA_URL)) {
    camera.end();
    return false;
  }
  upload.addHeader("Content-Type", contentType);
  addDeviceHeaders(upload);
  const int uploadStatus = upload.sendRequest("POST", imageStream, imageLength);
  const String response = uploadStatus > 0 ? upload.getString() : "";
  upload.end();
  camera.end();

  if (uploadStatus != HTTP_CODE_CREATED) {
    Serial.printf("Upload foto masuk gagal, HTTP %d\n", uploadStatus);
    return false;
  }

  JsonDocument responseBody;
  if (deserializeJson(responseBody, response) || !responseBody["path"].is<const char *>()) {
    Serial.println("Response upload tidak berisi path gambar.");
    return false;
  }

  photoPath = responseBody["path"].as<String>();
  return true;
}

bool printTicket(const String &ticketId) {
  WiFiClient printer;
  if (!printer.connect(PRINTER_HOST, PRINTER_PORT, 1500)) {
    Serial.println("Printer tidak terjangkau; palang tetap tertutup.");
    return false;
  }

  const uint8_t initialize[] = {0x1B, 0x40};
  if (printer.write(initialize, sizeof(initialize)) != sizeof(initialize)) {
    printer.stop();
    return false;
  }
  printer.println("PARKIR - TIKET MASUK");
  printer.println("Simpan tiket sampai keluar");
  printer.println(ticketId);

  // GS k 73 mencetak barcode CODE128. Prefix {B memilih subset B.
  String barcodeData = "{B" + ticketId;
  printer.write(static_cast<uint8_t>(0x1D));
  printer.write(static_cast<uint8_t>('k'));
  printer.write(static_cast<uint8_t>(73));
  printer.write(static_cast<uint8_t>(barcodeData.length()));
  printer.print(barcodeData);
  printer.println();
  printer.println();
  printer.write(static_cast<uint8_t>(0x1D));
  printer.write(static_cast<uint8_t>('V'));
  printer.write(static_cast<uint8_t>(66));
  printer.write(static_cast<uint8_t>(0));
  printer.flush();
  const bool connected = printer.connected();
  printer.stop();
  if (!connected) {
    Serial.println("Koneksi printer terputus saat mencetak.");
    return false;
  }
  return true;
}

bool createEntry(const String &rfidUid, const String &idempotencyKey, String &ticketId) {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("Server LAN tidak terhubung; palang tetap tertutup.");
    return false;
  }
  if (deviceApiKey.length() == 0) {
    Serial.println("Credential perangkat belum diset; jalankan SET_DEVICE_KEY=... via Serial.");
    return false;
  }

  String photoPath;
  if (!capturePhoto(photoPath)) {
    Serial.println("Kamera gagal mengambil foto.");
    return false;
  }

  JsonDocument requestBody;
  requestBody["gateId"] = gateId;
  requestBody["vehicleTypeId"] = VEHICLE_TYPE_ID;
  requestBody["idempotencyKey"] = idempotencyKey;
  if (photoPath.length() > 0) {
    requestBody["photoInPath"] = photoPath;
  } else {
    requestBody["photoInPath"] = nullptr;
  }
  if (rfidUid.length() > 0) {
    requestBody["rfidUid"] = rfidUid;
  } else {
    requestBody["rfidUid"] = nullptr;
  }

  String body;
  serializeJson(requestBody, body);
  HTTPClient http;
  http.setConnectTimeout(1000);
  http.setTimeout(1500);
  if (!http.begin(API_URL)) {
    return false;
  }
  http.addHeader("Content-Type", "application/json");
  addDeviceHeaders(http);
  const int statusCode = http.POST(body);
  const String response = statusCode > 0 ? http.getString() : "";
  http.end();

  if (statusCode != HTTP_CODE_CREATED && statusCode != HTTP_CODE_OK) {
    Serial.printf("API entry gagal, HTTP %d\n", statusCode);
    return false;
  }

  JsonDocument responseBody;
  if (deserializeJson(responseBody, response) || !responseBody["ticketId"].is<const char *>()) {
    Serial.println("Response API tidak berisi ticketId yang valid.");
    return false;
  }
  ticketId = responseBody["ticketId"].as<String>();
  return true;
}

void sendHeartbeat() {
  if (WiFi.status() != WL_CONNECTED || deviceApiKey.length() == 0) {
    return;
  }

  JsonDocument requestBody;
  requestBody["deviceId"] = gateId;
  requestBody["deviceType"] = "GATE_IN";
  requestBody["displayName"] = String(DEVICE_NAME) + " (" + gateId + ")";
  requestBody["firmwareVersion"] = FIRMWARE_VERSION;
  String body;
  serializeJson(requestBody, body);

  HTTPClient http;
  http.setConnectTimeout(750);
  http.setTimeout(1000);
  if (!http.begin(API_HEARTBEAT_URL)) {
    return;
  }
  http.addHeader("Content-Type", "application/json");
  addDeviceHeaders(http);
  const int statusCode = http.POST(body);
  if (statusCode != HTTP_CODE_NO_CONTENT) {
    Serial.printf("Heartbeat gate gagal, HTTP %d\n", statusCode);
  }
  http.end();
}

void openGate() {
  digitalWrite(GATE_RELAY_PIN, HIGH);
  gateIsOpen = true;
  vehicleCleared = false;
  gateOpenedAt = millis();
  Serial.println("Palang masuk dibuka.");
}

void closeGate() {
  digitalWrite(GATE_RELAY_PIN, LOW);
  gateIsOpen = false;
  Serial.println("Palang masuk ditutup.");
}

void issueTicket(const String &rfidUid) {
  if (!vehiclePresent()) {
    return;
  }

  String ticketId;
  if (pendingEntryKey.length() == 0) {
    pendingEntryKey = gateId + "-" + String(millis()) + "-" + String(esp_random(), HEX);
  }
  if (!createEntry(rfidUid, pendingEntryKey, ticketId)) {
    return;
  }
  pendingEntryKey = "";
  Serial.printf("Tiket dibuat: %s\n", ticketId.c_str());
  if (printTicket(ticketId)) {
    reportPrintStatus(ticketId, "PRINT_SENT", "TCP write selesai; konfirmasi fisik tidak tersedia");
    openGate();
  } else {
    reportPrintStatus(ticketId, "PRINT_FAILED", "Printer tidak mengonfirmasi koneksi/tulisan");
  }
}

void processGateConfiguration() {
  if (!Serial.available()) {
    return;
  }

  String command = Serial.readStringUntil('\n');
  command.trim();
  constexpr char gatePrefix[] = "SET_GATE_ID=";
  constexpr char keyPrefix[] = "SET_DEVICE_KEY=";

  if (command.startsWith(gatePrefix)) {
    String requestedId = command.substring(sizeof(gatePrefix) - 1);
    if (requestedId.length() == 0 || requestedId.length() > 20) {
      Serial.println("Gate ID harus 1-20 karakter.");
      return;
    }
    for (size_t index = 0; index < requestedId.length(); index++) {
      const char character = requestedId[index];
      if (!isAlphaNumeric(character) && character != '-' && character != '_' && character != '.') {
        Serial.println("Gate ID hanya boleh berisi huruf, angka, titik, garis bawah, dan tanda hubung.");
        return;
      }
    }

    gateId = requestedId;
    preferences.putString("gate_id", gateId);
    Serial.printf("Gate ID tersimpan: %s. Pastikan ID ini sudah didaftarkan di dashboard.\n", gateId.c_str());
    return;
  }

  if (command.startsWith(keyPrefix)) {
    String requestedKey = command.substring(sizeof(keyPrefix) - 1);
    if (requestedKey.length() < 16 || requestedKey.length() > 200) {
      Serial.println("Device key harus 16-200 karakter.");
      return;
    }
    for (size_t index = 0; index < requestedKey.length(); index++) {
      if (isSpace(requestedKey[index])) {
        Serial.println("Device key tidak boleh mengandung spasi.");
        return;
      }
    }

    deviceApiKey = requestedKey;
    preferences.putString("device_api_key", deviceApiKey);
    Serial.println("Device key tersimpan di NVS. Nilai tidak ditampilkan.");
    return;
  }

  Serial.println("Perintah tidak dikenal. Gunakan SET_GATE_ID=... atau SET_DEVICE_KEY=...");
}

void setup() {
  Serial.begin(115200);
  Serial.setTimeout(100);
  preferences.begin("parkir-gate", false);
  gateId = preferences.getString("gate_id", DEFAULT_GATE_ID);
  deviceApiKey = preferences.getString("device_api_key", "");
  Serial.printf("Gate ID aktif: %s\n", gateId.c_str());
  if (deviceApiKey.length() == 0) {
    Serial.println("PERINGATAN: device key belum diset. Jalankan SET_DEVICE_KEY=... via Serial Monitor.");
  }
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  pinMode(LOOP_SENSOR_PIN, INPUT_PULLUP);
  pinMode(GATE_RELAY_PIN, OUTPUT);
  digitalWrite(GATE_RELAY_PIN, LOW);

  SPI.begin();
  rfid.PCD_Init();

  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  Serial.print("Menghubungkan ke Wi-Fi LAN");
  const uint32_t startedAt = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - startedAt < 15000) {
    delay(250);
    Serial.print('.');
  }
  Serial.println();
  if (WiFi.status() == WL_CONNECTED) {
    Serial.print("IP ESP32: ");
    Serial.println(WiFi.localIP());
  } else {
    Serial.println("Wi-Fi belum terhubung; tiket tidak akan diterbitkan.");
  }
  lastWifiAttemptAt = millis();
}

void loop() {
  processGateConfiguration();
  ensureWifi();

  if (WiFi.status() == WL_CONNECTED &&
      (lastHeartbeatAt == 0 || millis() - lastHeartbeatAt >= HEARTBEAT_INTERVAL_MS)) {
    lastHeartbeatAt = millis();
    sendHeartbeat();
  }

  if (gateIsOpen) {
    const uint32_t openFor = millis() - gateOpenedAt;
    if (!vehiclePresent() && openFor >= GATE_MIN_OPEN_MS) {
      vehicleCleared = true;
    }
    if (vehicleCleared) {
      closeGate();
    } else if (openFor >= GATE_MAX_OPEN_MS) {
      Serial.println("Batas waktu palang terbuka tercapai; palang ditutup paksa.");
      closeGate();
    }
    return;
  }

  if (!vehiclePresent() && pendingEntryKey.length() > 0) {
    pendingEntryKey = "";
  }

  const String uid = readRfidUid();
  if (uid.length() > 0) {
    issueTicket(uid);
    return;
  }
  if (buttonPressed()) {
    issueTicket("");
  }
}