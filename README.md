# Sistem Parkir LAN

Implementasi contoh bertahap dari PRD: PostgreSQL, REST API ASP.NET Core, aplikasi kasir WPF, dan firmware ESP32.

## 1. Database PostgreSQL

Jalankan schema pada database kosong:

```sh
psql "Host=localhost;Database=parkir;Username=postgres" -f database/schema.sql
```

Schema menambahkan tarif grace period, denda karcis hilang, dan tarif maksimum harian di `vehicle_types`. Schema tidak membuat akun atau password default. Buat akun lewat utilitas provisioning setelah database siap:

```sh
ConnectionStrings__Parking='Host=127.0.0.1;Database=parkir;Username=postgres;Password=GANTI_PASSWORD' \
  dotnet run --project tools/Parkir.UserProvisioner
```

Utilitas meminta username, nama, role (`ADMIN`/`CASHIER`), dan password tersembunyi. Buat setidaknya satu akun `CASHIER` untuk memakai POS.

Untuk membuat administrator contoh development dengan password acak yang hanya dicetak setelah akun tersimpan:

```sh
DOTNET_ROLL_FORWARD=Major ConnectionStrings__Parking='Host=127.0.0.1;Database=parkir;Username=postgres;Password=GANTI_PASSWORD' \
  dotnet run --project tools/Parkir.UserProvisioner -- --example-admin
```

Kredensial akun demo development yang sudah dibuat pada database lokal:

- Username: `admin-demo`
- Password: `nlXFkov2FZGEaxnPhQ8pv9Kuq4rQhBzn`

Password disimpan di README atas permintaan untuk kemudahan pengujian. Jangan gunakan kredensial ini di produksi atau pada database/jaringan bersama; bila repository dibagikan, hapus password dari README dan buat akun produksi dengan password baru. Jika database lokal dibuat ulang, jalankan utilitas di atas lagi untuk menghasilkan akun baru.

## 2. REST API

Persyaratan: .NET 8 SDK dan PostgreSQL. Workspace Linux ini memiliki runtime .NET 9/10, bukan runtime .NET 8; pada container ini set `DOTNET_ROLL_FORWARD=Major` saat menjalankan target net8.0. Untuk deployment biasa, pasang runtime .NET 8 yang sesuai. Atur connection string melalui environment variable, lalu jalankan API pada port yang dipakai POS dan ESP32:

```sh
export DOTNET_ROLL_FORWARD=Major
export ConnectionStrings__Parking='Host=127.0.0.1;Port=5432;Database=parkir;Username=postgres;Password=GANTI_PASSWORD'
export Security__ApiKey='GANTI_DENGAN_KUNCI_ACAK_PANJANG'
export Security__JwtSigningKey='GANTI_DENGAN_SECRET_ACAK_MINIMAL_32_BYTE'
export Storage__ImageRoot='/var/lib/parkir/images'
dotnet run --project backend/Parkir.Api --urls http://0.0.0.0:5080
```

API key hanya diwajibkan untuk endpoint perangkat `/api/entries` dan `/api/members/*`; login memakai rate limit dan endpoint admin/kasir memakai JWT. Secret JWT wajib berbeda dan minimal 32 byte. Simpan secret di secret store dan batasi akses port API melalui firewall/VLAN. Jika database sudah memakai schema lama, jalankan migrasi `002_add_checkout_idempotency.sql` lalu `003_add_audit_logs.sql` satu kali.

Gambar disimpan di bawah `Storage__ImageRoot/YYYY-MM-DD/` dengan nama GUID dan tidak disajikan sebagai static file. Upload menerima JPEG, PNG, atau WebP sampai 5 MB; endpoint masuk memerlukan API key, sedangkan upload/ambil foto keluar memerlukan JWT kasir. Pastikan direktori storage masuk strategi backup dan kontrol akses filesystem.

Endpoint utama:

| Method | Endpoint | Fungsi |
| --- | --- | --- |
| `POST` | `/api/auth/login` | Login akun dan menerbitkan JWT 8 jam |
| `POST` | `/api/entries` | Membuat tiket masuk, dengan `rfidUid` opsional |
| `GET` | `/api/transactions/{ticketId}/quote` | Menghitung durasi dan tagihan (role `CASHIER`) |
| `POST` | `/api/transactions/{ticketId}/checkout` | Menutup tiket dan mencatat pembayaran (role `CASHIER`) |
| `GET` | `/api/members/{rfidUid}/verify` | Memverifikasi member aktif |
| `GET` | `/api/admin/dashboard` | Statistik kendaraan dan pendapatan hari ini (role `ADMIN`) |
| `GET`, `POST`, `PUT` | `/api/admin/vehicle-types` | Membaca dan mengatur tarif (role `ADMIN`) |
| `GET`, `POST`, `PUT` | `/api/admin/members` | Membaca, mendaftarkan, dan mengubah status member (role `ADMIN`) |
| `GET` | `/api/admin/reports?from=YYYY-MM-DD&to=YYYY-MM-DD` | Laporan harian, maksimal 366 hari (role `ADMIN`) |
| `GET` | `/api/admin/reports/export.csv?from=...&to=...` | Unduh laporan CSV (role `ADMIN`) |
| `POST` | `/api/media/entries` | Upload JPEG/PNG/WebP masuk (API key perangkat, maksimal 5 MB) |
| `POST` | `/api/media/exits` | Upload foto keluar (JWT `CASHIER`, maksimal 5 MB) |
| `GET` | `/api/media/{yyyy-MM-dd}/{fileName}` | Ambil foto masuk untuk perbandingan visual (JWT `CASHIER`) |

Dashboard admin tersedia di root server, misalnya `http://localhost:5080/`. Login menggunakan body `{"username":"...","password":"..."}` tanpa API key perangkat. POS hanya menerima akun `CASHIER`; dashboard hanya menerima akun `ADMIN`. ID kasir diambil dari JWT dan tidak diterima dari body checkout.

Login dibatasi 5 percobaan per alamat IP dalam jendela 5 menit. Percobaan login sukses/gagal dicatat ke `audit_logs`; checkout dan audit checkout ditulis dalam transaksi database yang sama.

Contoh tiket masuk:

```sh
curl -X POST http://localhost:5080/api/entries \
  -H 'Content-Type: application/json' \
  -d '{"gateId":"GATE-IN-01","vehicleTypeId":1,"photoInPath":null,"rfidUid":null}'
```

Checkout reguler mengirim `gateId`, `paymentMethod`, `lostTicket:false`, `identityVerified:false`, `photoOutPath:null`, dan `idempotencyKey` berupa UUID. Karcis hilang harus mengirim `lostTicket:true` serta `identityVerified:true`. Kunci idempotency yang sama akan mengembalikan hasil checkout pertama, sehingga retry setelah timeout tidak menggandakan transaksi.

## 3. POS kasir WPF

Build dan jalankan pada Windows dengan .NET 8 SDK:

```powershell
dotnet build pos/Parkir.Pos/Parkir.Pos.csproj
dotnet run --project pos/Parkir.Pos/Parkir.Pos.csproj
```

Atur `PARKIR_API_KEY` dan, bila perlu, `PARKIR_API_URL` sebelum menjalankan POS. Jendela login hanya mengizinkan role `CASHIER`; token berlaku delapan jam. Pemindai barcode yang bekerja sebagai keyboard HID dapat langsung mengisi nomor tiket dan menekan Enter. POS menampilkan foto masuk dan mengunggah foto keluar sebelum checkout. Jika `PARKIR_OUT_CAMERA_URL` tidak diatur, petugas dapat memilih file JPEG/PNG/WebP; URL kamera harus mengembalikan satu snapshot dengan Content-Type dan Content-Length. Checkout disimpan lebih dahulu di SQLite pada `%LOCALAPPDATA%/Parkir/Pos` dan dikirim ulang tiap 10 detik dengan idempotency key yang sama. Jika server belum mengonfirmasi, jangan buka palang; jangan anggap pembayaran/tiket selesai sampai POS menampilkan konfirmasi server. Printer struk dan relay USB/serial masih memerlukan adapter sesuai perangkat.

## 4. Gerbang masuk ESP32

Project PlatformIO ada di `firmware/gate_in`:

```sh
cd firmware/gate_in
pio run
pio run --target upload
pio device monitor
```

Sesuaikan SSID, password, URL API, `API_MEDIA_URL`, `API_KEY`, `CAMERA_SNAPSHOT_URL`, alamat printer, dan `VEHICLE_TYPE_ID` pada `gate_in.ino`. Kamera harus menyediakan endpoint snapshot HTTP yang mengembalikan JPEG/PNG/WebP dengan Content-Length. Pin default: tombol GPIO 27, sensor loop GPIO 25, relay GPIO 26, RFID SS GPIO 5 dan RST GPIO 4. Input tombol/sensor diasumsikan aktif LOW, sedangkan relay diasumsikan aktif HIGH. Verifikasi level listrik dan pin aktual sebelum menghubungkan perangkat.

Printer tiket menggunakan ESC/POS melalui TCP port 9100. Pembukaan palang hanya terjadi setelah server membuat tiket dan printer menerima koneksi. Palang menutup setelah sensor menyatakan area kosong; gunakan juga kontrol keselamatan/fotokel independen dan interlock hardware yang sesuai. Kegagalan Wi-Fi/API berarti tiket tidak diterbitkan dan palang tidak dibuka.

## Batas contoh

JWT dan role memisahkan akses kasir/admin; API key mengautentikasi endpoint perangkat. API masih menggunakan HTTP pada contoh; pasang TLS dan mekanisme rotasi secret sebelum produksi. Audit mencakup login, checkout, dan perubahan tarif/member. Dashboard menyediakan laporan harian rentang tanggal dan ekspor CSV. Endpoint kamera menggunakan HTTP snapshot generik, sehingga autentikasi, format, dan alamat perlu disesuaikan dengan kamera aktual. Integrasi printer/relay POS masih memerlukan adapter perangkat. Outbox POS hanya menyimpan checkout untuk retry dan tidak membuka palang secara offline. Firmware menggunakan Wi-Fi; ganti transport jaringan jika instalasi memakai Ethernet W5500.