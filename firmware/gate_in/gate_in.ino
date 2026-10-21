#include <ArduinoJson.h>
#include <HTTPClient.h>
#include <MFRC522.h>
#include <SPI.h>
#include <WiFi.h>
#include <WiFiClient.h>

// Sesuaikan nilai ini dengan jaringan, perangkat, dan vehicle_type_id database.
constexpr char WIFI_SSID[] = "PARKIR-LAN";
constexpr char WIFI_PASSWORD[] = "ganti-password";
constexpr char API_URL[] = "http://192.168.1.10:5080/api/entries";
constexpr char API_MEDIA_URL[] = "http://192.168.1.10:5080/api/media/entries";
constexpr char API_HEARTBEAT_URL[] = "http://192.168.1.10:5080/api/devices/heartbeat";
constexpr char API_KEY[] = "ganti-api-key";
constexpr char CAMERA_SNAPSHOT_URL[] = "http://192.168.1.60/snapshot.jpg";
constexpr char GATE_ID[] = "GATE-IN-01";
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
constexpr uint32_t HEARTBEAT_INTERVAL_MS = 20000;

MFRC522 rfid(RFID_SS_PIN, RFID_RST_PIN);

bool gateIsOpen = false;
bool vehicleCleared = false;
bool lastButtonReading = HIGH;
bool stableButtonState = HIGH;
uint32_t lastButtonChangeAt = 0;
uint32_t gateOpenedAt = 0;
uint32_t lastHeartbeatAt = 0;

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
  upload.addHeader("X-API-Key", API_KEY);
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
  printer.write(initialize, sizeof(initialize));
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
  printer.stop();
  return true;
}

bool createEntry(const String &rfidUid, String &ticketId) {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("Server LAN tidak terhubung; palang tetap tertutup.");
    return false;
  }

  String photoPath;
  if (!capturePhoto(photoPath)) {
    Serial.println("Kamera gagal mengambil foto.");
    return false;
  }

  JsonDocument requestBody;
  requestBody["gateId"] = GATE_ID;
  requestBody["vehicleTypeId"] = VEHICLE_TYPE_ID;
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
  http.addHeader("X-API-Key", API_KEY);
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
  if (WiFi.status() != WL_CONNECTED) {
    return;
  }

  JsonDocument requestBody;
  requestBody["deviceId"] = GATE_ID;
  requestBody["deviceType"] = "GATE_IN";
  requestBody["displayName"] = DEVICE_NAME;
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
  http.addHeader("X-API-Key", API_KEY);
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
  if (!createEntry(rfidUid, ticketId)) {
    return;
  }
  Serial.printf("Tiket dibuat: %s\n", ticketId.c_str());
  if (printTicket(ticketId)) {
    openGate();
  }
}

void setup() {
  Serial.begin(115200);
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
}

void loop() {
  if (WiFi.status() == WL_CONNECTED &&
      (lastHeartbeatAt == 0 || millis() - lastHeartbeatAt >= HEARTBEAT_INTERVAL_MS)) {
    lastHeartbeatAt = millis();
    sendHeartbeat();
  }

  if (gateIsOpen) {
    if (!vehiclePresent()) {
      vehicleCleared = true;
    }
    if (vehicleCleared) {
      closeGate();
    }
    return;
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