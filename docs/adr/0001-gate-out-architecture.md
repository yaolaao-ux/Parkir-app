# ADR-0001 — Arsitektur Kontrol Gate-Out

- Status: **PROPOSED / MENUNGGU KEPUTUSAN PEMILIK SISTEM**
- Tanggal: 18 November 2026
- Konteks: Parkir-app, POS Windows, Gate-Out, relay serial, API LAN

## Konteks

Saat ini POS Windows mengendalikan relay Gate-Out melalui port serial setelah checkout dikonfirmasi server. API hanya menerima eventcommand gate (`SENT`, `ACKNOWLEDGED`, `EXECUTED`, `FAILED`) untuk audit. Tidak ada bukti posisi fisik palang dan tidak ada controller gate jaringan terpisah.

## Keputusan yang harus diambil

Pilih salah satu:

1. **Opsi A — POS tetap menjadi pengendali relay.** POS mengirim pulse serial ke relay module; server hanya mencatat event.
2. **Opsi B — Controller gate terpisah.** POS/API mengirim command ke controller; controller menggerakkan relay dan mengirim acknowledgement/feedback.

Keputusan belum diambil. Jangan mengubah arsitektur sebelum pemilik sistem menetapkan pilihan dan teknisi hardware menyetujui wiring.

## Opsi A — POS sebagai pengendali relay

**Kelebihan**
- Mengikuti implementasi repository saat ini.
- Tidak memerlukan controller/firmware gate-out tambahan.

**Risiko**
- Gate bergantung pada PC POS, Windows, driver serial, dan proses aplikasi.
- Tidak ada feedback posisi gate.
- PC restart, USB lepas, atau aplikasi crash dapat membuat gate tidak dapat dibuka.
- Keamanan fisik tetap bergantung pada interlock hardware gate controller.

**Syarat minimum**
- Relay module terisolasi dan hanya memakai kontak kering ke input kontrol gate.
- Kabel/port serial diberi label dan diuji terhadap noise.
- Ada prosedur manual/emergency dan audit operator.
- POS mengirim event `SENT`/`EXECUTED`/`FAILED`; `EXECUTED` hanya berarti perintah terkirim.

## Opsi B — Controller gate terpisah

**Kelebihan**
- Fungsi kasir terpisah dari kontrol gate.
- Command, ACK, feedback, dan status perangkat dapat dipantau terpusat.

**Risiko**
- Perlu provisioning device, firmware/controller baru, proteksi jaringan, dan uji hardware.
- Perlu keputusan protokol (HTTP/MQTT/serial) dan model keamanan.

**Syarat minimum**
- Command memiliki ID, expiry, gate binding, dan acknowledgement.
- Controller menolak command duplikat/kedaluwarsa.
- Status `EXECUTED` hanya diberikan setelah output/feedback yang sesuai.
- Safety interlock tetap hardware.

## Pekerjaan wajib untuk kedua opsi

- [ ] Diagram wiring aktual dan rating relay/driver.
- [ ] Daftar perangkat, gate, IP, dan kredensial (tanpa menyimpan key di dokumen).
- [ ] Emergency stop, photocell/safety edge, dan anti-crush independen software.
- [ ] Prosedur manual saat POS/server/jaringan/controller gagal.
- [ ] Uji command duplikat, expiry, gate salah, dan feedback hilang.
- [ ] Audit command dan hasilnya.
- [ ] Persetujuan teknisi kompeten sebelum menggerakkan barrier.

## Konsekuensi jika keputusan ditunda

Pilot hanya boleh dilakukan pada bench dengan barrier terisolasi dari kendaraan/manusia. Jangan menyatakan Gate-Out siap produksi tanpa keputusan ini dan bukti uji hardware.
