# Wiring Gate-Out (Implementasi Saat Ini — Opsi A)

Dokumen ini adalah template commissioning, bukan instruksi instalasi final. Gate-Out saat ini dikendalikan POS Windows melalui relay serial. Keputusan arsitektur ada di `docs/adr/0001-gate-out-architecture.md`.

## Komponen

- PC POS dengan port serial/USB-serial.
- Relay module yang sesuai dengan level logika/antarmuka port.
- Gate controller dengan input kontrol dry-contact.
- Emergency stop, photocell/safety edge, dan interlock hardware dari pabrikan gate.

## Prinsip keselamatan

- Jangan menghubungkan PC, USB-serial, atau relay langsung ke motor, AC, atau output bertegangan gate controller.
- Gunakan hanya kontak kering `COM/NO` ke input kontrol yang diizinkan pabrikan.
- Emergency stop dan sensor anti-crush harus independen dari POS dan software.
- Saat PC boot/restart atau kabel lepas, relay harus berada pada kondisi aman (tidak memberi pulse).
- Verifikasi polaritas, rating kontak, isolasi, grounding, dan proteksi lonjakan dari datasheet aktual.

## Konfigurasi POS

```text
PARKIR_RELAY_PORT=COM3
PARKIR_RELAY_BAUD=9600
PARKIR_RELAY_OPEN_HEX=<byte hex protokol relay aktual>
```

`PARKIR_RELAY_OPEN_HEX` bergantung pada modul relay; jangan menyalin nilai dari contoh tanpa datasheet. Uji dengan beban/aktuator terlepas.

## Bench checklist

- [ ] Relay tidak aktif saat boot/restart.
- [ ] Perintah pulse hanya sekali per checkout.
- [ ] Gate controller menerima pulse dan bergerak sesuai desain.
- [ ] Emergency stop menghentikan gerakan.
- [ ] Sensor area terhalang mencegah penutupan berbahaya.
- [ ] Kabel serial lepas tidak menyebabkan gerakan tak terduga.
- [ ] Event `SENT`/`EXECUTED`/`FAILED` tercatat di API.
- [ ] Prosedur manual tersedia jika POS/relay gagal.

## Batasan

`EXECUTED` pada API berarti perintah relay terkirim, bukan bukti posisi gate. Feedback posisi hanya dapat dinyatakan jika controller menyediakan sensor/feedback yang terhubung dan diuji.
