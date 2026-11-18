# Hardware-in-the-Loop Test Plan

Dokumen ini adalah prosedur uji, bukan bukti hasil. Status awal semua skenario: **NOT RUN**.

## 1. Tujuan

Memvalidasi Gate-In, Gate-Out, printer, sensor, reconnect, command/ACK, dan keselamatan pada bench sebelum pilot. Pengujian hanya boleh dilakukan teknisi kompeten dengan barrier terisolasi dari kendaraan/manusia.

## 2. Peran dan alat

- Teknisi hardware/electrical yang kompeten.
- Operator POS.
- Penguji API/network.
- Barrier gate dengan emergency stop, photocell/safety edge, dan interlock hardware.
- Relay module terisolasi, multimeter, power supply sesuai rating.
- Printer thermal, kertas, scanner/barcode.
- Jaringan uji terpisah; database staging; device credential uji.

## 3. Pre-check wiring

- [ ] Gate controller dalam keadaan tidak bertegangan saat wiring diubah.
- [ ] Relay COM/NO hanya kontak kering ke input kontrol; tidak ada tegangan motor/AC.
- [ ] Emergency stop dan safety edge independen berfungsi tanpa software.
- [ ] Polaritas sensor loop/photoeye sesuai; output 12/24 V tidak langsung ke GPIO.
- [ ] ESP32 dan relay module mendapat catu sesuai datasheet.
- [ ] Semua kabel diberi label; tidak ada kabel terbuka.
- [ ] Firmware terbaru, gate ID benar, dan device key sudah diset via NVS (bukan source code).

## 4. Gate-In

| No | Skenario | Hasil yang diharapkan | Status |
|---|---|---|---|
| GI-01 | Boot/reset | Relay terbuka, gate tidak bergerak tanpa perintah | NOT RUN |
| GI-02 | Tombol tiket | Satu sesi, satu tiket, gate terbuka setelah print sukses | NOT RUN |
| GI-03 | RFID member aktif | Satu sesi member; anti-passback menolak sesi kedua | NOT RUN |
| GI-04 | RFID invalid/expired | Ditolak; tidak ada tiket/gate | NOT RUN |
| GI-05 | Respons entry hilang lalu retry | Satu tiket, bukan dua | NOT RUN |
| GI-06 | Printer offline setelah sesi dibuat | `PRINT_FAILED`; gate tidak membuka; tiket tidak ganda | NOT RUN |
| GI-07 | Kertas habis/cover terbuka (jika terdeteksi) | Status jujur; operator diberi instruksi | NOT RUN |
| GI-08 | Wi-Fi putus 30 detik | Reconnect backoff; loop sensor tidak hang | NOT RUN |
| GI-09 | Server mati saat entry | Tidak ada tiket/gate; retry aman setelah server pulih | NOT RUN |
| GI-10 | Sensor loop stuck LOW | Gate menutup paksa sesuai batas; alarm/log | NOT RUN |
| GI-11 | Sensor loop stuck HIGH | Gate tidak menutup saat kendaraan; safety hardware aktif | NOT RUN |

## 5. Gate-Out

| No | Skenario | Hasil yang diharapkan | Status |
|---|---|---|---|
| GO-01 | Checkout tunai sukses | Pembayaran tercatat, event `SENT` lalu `EXECUTED` | NOT RUN |
| GO-02 | Printer struk gagal | Pembayaran tetap tercatat; operator melihat status | NOT RUN |
| GO-03 | Relay tidak tersedia | Event `FAILED`; prosedur manual dengan audit | NOT RUN |
| GO-04 | POS crash setelah pembayaran | Tidak ada pembayaran ganda; recovery jelas | NOT RUN |
| GO-05 | Server timeout saat checkout | Outbox menyimpan; gate tidak dibuka sampai konfirmasi | NOT RUN |
| GO-06 | Dua kasir checkout tiket sama | Hanya satu pembayaran final | NOT RUN |
| GO-07 | Command gate duplikat | Ditolak/idempoten; tidak menggerakkan gate dua kali | NOT RUN |
| GO-08 | Gate command expired | Ditolak/ditandai; tidak ada gerakan tak terduga | NOT RUN |
| GO-09 | Kabel serial lepas | `FAILED`/status jelas; gate tidak terbuka | NOT RUN |
| GO-10 | Emergency stop | Gerakan berhenti sesuai desain hardware | NOT RUN |

## 6. Keselamatan fisik

- [ ] Uji emergency stop saat gate bergerak.
- [ ] Uji safety edge/photocell saat kendaraan/objek di area lintasan.
- [ ] Uji sensor mati/stuck, kabel putus, dan noise.
- [ ] Uji power loss/restart controller; gate tidak bergerak tak terduga.
- [ ] Uji gate sudah terbuka, sedang bergerak, terhalang, dan gagal bergerak.
- [ ] Pastikan tidak ada kendaraan/manusia pada area berbahaya saat uji.

## 7. Bukti yang harus disimpan

- Foto wiring dan label port.
- Versi firmware, commit aplikasi, dan waktu uji.
- Log API dengan correlation ID.
- Screenshot status POS/queue.
- Hasil command gate dan status print.
- Tanda tangan teknisi/operator dan daftar kegagalan.

## 8. Kriteria lulus

- Tidak ada tiket/pembayaran ganda.
- Gate tidak membuka tanpa perintah valid.
- Gate tidak menutup pada area terhalang; emergency stop bekerja.
- Reconnect dan retry tidak menduplikasi operasi.
- Semua kegagalan printer/relay memiliki status dan prosedur manual.
- Seluruh skenario P0 di atas lulus; jika tidak, pilot ditunda.
