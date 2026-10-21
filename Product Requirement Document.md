# Product Requirement Document (PRD)
## Sistem Manajemen Parkir Berbasis Jaringan Lokal (LAN Parking System)

---

## 1. Ringkasan Eksekutif (Executive Summary)
**Sistem Manajemen Parkir Berbasis LAN** adalah aplikasi terdistribusi yang dirancang untuk mengelola operasional area parkir secara efisien, stabil, dan berkecepatan tinggi. Sistem ini menggunakan arsitektur *Client-Server* lokal terpusat yang menghubungkan modul *Gate In* (Mikrokontroler/ESP32), Pos Kasir/ *Gate Out* (Aplikasi Desktop), Server Utama (Database & API Service), dan Panel Manajemen Admin secara *real-time* tanpa ketergantungan pada koneksi internet.

---

## 2. Arsitektur Sistem & Tech Stack

### 2.1 Arsitektur Sistem
* **Model:** Client-Server Terpusat (Distributed Architecture via LAN)
* **Konektivitas:** TCP/IP (Kabel Ethernet / LAN)
* **Komponen:**
  1. **Server Utama:** Database Server & Local Web API Service.
  2. **Gate In (Pos Masuk):** Standalone Microcontroller + Printer Thermal & Palang Pintu.
  3. **Gate Out (Pos Keluar / Kasir):** Aplikasi Desktop Client + Scanner + Dual Monitor View + Printer + Relay Palang.
  4. **Panel Admin:** Web-based Dashboard Lokal.

### 2.2 Tech Stack Rekomendasi
* **Database:** PostgreSQL
* **Backend Server / API:** Go (Golang) / C# (.NET Core Web API)
* **Aplikasi Desktop Pos Kasir:** C# (.NET / WPF atau Windows Forms)
* **Mikrokontroler Gate In:** ESP32 (dengan modul Ethernet W5500 / Wi-Fi Lokal)
* **Dashboard Admin:** Web Front-End (HTML5, Tailwind CSS, JavaScript / Vue.js)
* **Protokol Komunikasi:** REST API / WebSockets / TCP Sockets

---

## 3. Komponen & Spesifikasi Modul

### 3.1 Modul Server (Pusat Data & Layanan API)
* **Deskripsi:** Berjalan sebagai *background service* di PC Server utama.
* **Fitur Utama:**
  * Pengelolaan Database Transaksi Parkir (`transactions`), Member (`members`), Jenis Kendaraan & Tarif (`vehicle_types`), dan Pengguna (`users`).
  * Penyimpanan Terpusat File Foto Masuk (`/storage/images/YYYY-MM-DD/`).
  * Penyediaan Endpoint API untuk transaksi masuk, perhitungan tarif, verifikasi RFID, dan pencatatan keluar.
  * Logging aktivitas sistem dan koneksi perangkat LAN secara *real-time*.

### 3.2 Modul Gate In (Pos Masuk - Mikrokontroler ESP32)
* **Deskripsi:** Modul perangkat keras mandiri untuk mencetak tiket dan memicu palang masuk.
* **Perangkat Terhubung:** Tombol Cetak, Sensor Loop Detector, Printer Thermal (Serial/LAN), RFID Reader, Modul Relay Palang, Kamera IP/USB.
* **Alur Operasional:**
  1. Sensor mendeteksi kendaraan berada di gerbang.
  2. Pengunjung menekan tombol tiket ATAU menempelkan kartu RFID member.
  3. ESP32 menangkap sinyal, meminta Kamera meng-capture foto, lalu mengirim request `INSERT` ke Server via LAN.
  4. Server memberikan tanggapan (*response*) berupa Nomor Tiket Unik.
  5. ESP32 memicu Printer Thermal untuk mencetak Tiket Barcode.
  6. ESP32 mengirim sinyal ke Modul Relay untuk Buka Palang Pintu.
  7. Setelah kendaraan lewat (direspon oleh Sensor Loop), palang otomatis menutup.

### 3.3 Modul Gate Out / Kasir (Aplikasi Desktop)
* **Deskripsi:** Aplikasi desktop GUI berkecepatan tinggi yang dioperasikan oleh petugas kasir pos keluar.
* **Fitur Utama:**
  * **Input Cepat:** Scan Barcode Tiket via Barcode Reader (HID Keyboard Mode) atau *input manual*.
  * **Dual Image View (Perbandingan Visual):** Tampilan berdampingan antara **Foto Masuk** (diunduh dari Server via LAN) dan **Kamera Live Out** untuk pencegahan kecurangan/penukaran kendaraan.
  * **Kalkulasi Tarif Otomatis:** Perhitungan durasi dan biaya berdasarkan selisih waktu masuk-keluar secara presisi.
  * **Penanganan Kasus Khusus:**
    * Tombol *Karcis Hilang* (mengenakan Denda Tetap + verifikasi identitas).
    * Tap Kartu RFID Member (langsung memverifikasi saldo/status aktif tanpa biaya harian).
  * **Cetak Struk:** Mengirim perintah cetak langsung ke Printer Thermal POS via ESC/POS Commands.
  * **Buka Palang:** Mengirim sinyal perintah ke Serial Relay / USB Relay untuk membuka palang keluar.

### 3.4 Modul Admin / Dashboard Management (Web-Based)
* **Deskripsi:** Antarmuka berbasis web untuk pengelola area parkir (dapat diakses via browser di PC Admin pada jaringan LAN yang sama).
* **Fitur Utama:**
  * **Dashboard Real-Time:** Menampilkan pendapatan harian, total kendaraan masuk/keluar, dan kapasitas parkir aktif.
  * **Manajemen Tarif Flexible:** Pengaturan tarif flat, tarif progresif per jam, denda tiket hilang, dan masa tenggang (*grace period*).
  * **Manajemen Member:** Pendaftaran kartu RFID, data pemilik, masa aktif langganan, dan pembaharuan saldo/paket.
  * **Monitoring Perangkat LAN:** Status indikator koneksi (Online/Offline) PC Kasir, ESP32 Pos Masuk, dan Database Server.
  * **Laporan & Export Data:** Laporan keuangan harian, mingguan, bulanan, serta ekspor data ke format PDF / Excel.

---

## 4. Rancangan Basis Data (Database Schema)

```sql
-- 1. Tabel Jenis Kendaraan & Tarif
CREATE TABLE vehicle_types (
    id SERIAL PRIMARY KEY,
    type_name VARCHAR(50) NOT NULL, -- Contoh: Mobil, Motor, Truk
    first_hour_rate DECIMAL(10,2) NOT NULL,
    hourly_rate DECIMAL(10,2) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 2. Tabel Member (Langganan)
CREATE TABLE members (
    id SERIAL PRIMARY KEY,
    rfid_uid VARCHAR(100) UNIQUE NOT NULL,
    full_name VARCHAR(100) NOT NULL,
    license_plate VARCHAR(20) NOT NULL,
    vehicle_type_id INT REFERENCES vehicle_types(id),
    expired_date DATE NOT NULL,
    status VARCHAR(20) DEFAULT 'ACTIVE', -- ACTIVE, EXPIRED, SUSPENDED
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 3. Tabel Transaksi Parkir
CREATE TABLE transactions (
    ticket_id VARCHAR(50) PRIMARY KEY, -- Format: TKT-YYYYMMDD-XXXX
    vehicle_type_id INT REFERENCES vehicle_types(id),
    gate_in_id VARCHAR(20) NOT NULL,
    gate_out_id VARCHAR(20),
    time_in TIMESTAMP NOT NULL,
    time_out TIMESTAMP,
    duration_minutes INT,
    photo_in_path VARCHAR(255),
    photo_out_path VARCHAR(255),
    total_fee DECIMAL(10,2) DEFAULT 0.00,
    penalty_fee DECIMAL(10,2) DEFAULT 0.00,
    payment_status VARCHAR(20) DEFAULT 'UNPAID', -- UNPAID, PAID, MEMBER, LOST_TICKET
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 4. Tabel Pengguna System (Kasir & Admin)
CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    full_name VARCHAR(100) NOT NULL,
    role VARCHAR(20) NOT NULL, -- ADMIN, CASHIER
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

---

## 5. Logika Perhitungan Tarif (Tariff Calculation Logic)

1. **Inisialisasi:**
   $$Durasi (Menit) = \frac{\text{TimeOut} - \text{TimeIn}}{60}$$
2. **Aturan Masa Tenggang (Grace Period):**
   * Jika $Durasi \le 5 \text{ Menit}$, Total Biaya = $\text{Rp 0}$ (Kendaraan sekadar lewat/salah masuk).
3. **Aturan Tarif Reguler:**
   * Konversi Durasi ke Jam (Pembulatan ke Atas): $Jam = \lceil Durasi / 60 \rceil$.
   * Jika $Jam \le 1$: $\text{Total Biaya} = FirstHourRate$.
   * Jika $Jam > 1$: $\text{Total Biaya} = FirstHourRate + ((Jam - 1) \times HourlyRate)$.
4. **Aturan Karcis Hilang:**
   * $\text{Total Biaya} = PenaltyFee + \text{Tarif Maksimal Hari Ini}$.

---

## 6. Persyaratan Non-Fungsional (Non-Functional Requirements)

* **Kecepatan & Latensi:** 
  * Waktu tanggap (*response time*) pembuatan tiket di Pos Masuk $< 500\text{ ms}$.
  * Waktu kalkulasi tarif dan pencarian data saat scan tiket di Pos Keluar $< 200\text{ ms}$.
* **Keandalan & Stabilitas:**
  * Aplikasi Kasir Desktop harus stabil dan sanggup berjalan terus menerus (*continuous operation*) 24/7 tanpa *memory leak*.
* **Keamanan:**
  * Setiap tiket yang sudah berstatus `PAID` tidak dapat digunakan kembali (*anti-passback*).
  * Hak akses menu terpisah tegas antara `CASHIER` dan `ADMIN`.
* **Kemampuan Bekerja Offline (Fault Tolerance):**
  * Pos Keluar menyimpan *queue* (antrean) transaksi sementara jika jaringan LAN terputus sejenak, dan otomatis melakukan sinkronisasi begitu LAN kembali terhubung.

---

## 7. Instruksi Implementasi untuk Agent AI
SaatAgent AI diminta membuat kode untuk proyek ini:
1. Prioritaskan pembuatan struktur Database PostgreSQL terlebih dahulu sesuai skema.
2. Buat REST API backend service menggunakan Go/C# yang melayani komunikasi JSON antar modul.
3. Bangun Aplikasi Desktop Kasir menggunakan C# (.NET WPF/Windows Forms) dengan UI yang bersih, *responsive*, dan mendukung *keyboard navigation* tanpa tergantung pada *mouse*.
4. Gunakan arsitektur *clean code* dengan pemisahan lapisan *Controller*, *Service*, dan *Repository*.
