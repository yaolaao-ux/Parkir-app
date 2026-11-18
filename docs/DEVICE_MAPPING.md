# Pemetaan Perangkat dan Gate

Isi tabel ini saat commissioning. Jangan menulis API key, password, atau private key di dokumen/repository.

| Gate ID | Tipe | Device ID | Peran | IP/MAC | Firmware | Kredensial dibuat | Terakhir diuji |
|---|---|---|---|---|---|---|---|
| GATE-IN-01 | GATE_IN | GATE-IN-01 | ESP32 Gate-In |  |  | [ ] |  |
| GATE-OUT-01 | GATE_OUT | GATE-OUT-01 | POS/relay |  |  | [ ] |  |
| GATE-IN-02 | GATE_IN | GATE-IN-02 | ESP32 Gate-In |  |  | [ ] |  |

## Aturan

- `Device ID` untuk gate harus sama dengan `Gate ID` yang didaftarkan admin. POS memakai `PARKIR_DEVICE_ID`/`PARKIR_GATE_ID`.
- Kredensial diterbitkan per device melalui `POST /api/admin/devices/{deviceId}/credential`; key hanya ditampilkan sekali.
- Satu key tidak boleh dipakai beberapa perangkat.
- Perangkat yang diganti harus dicabut kredensial lamanya.
- Catat versi firmware dan waktu uji, bukan nilai kredensial.
- Gate-Out yang masih memakai relai serial POS harus dicatat sebagai Opsi A pada ADR-0001.

## Checklist commissioning

- [ ] Gate ID di NVS/perangkat benar.
- [ ] Gate terdaftar dan aktif di dashboard admin.
- [ ] Kredensial device diterbitkan dan diuji.
- [ ] Heartbeat terlihat online.
- [ ] Entry/checkout test di staging berhasil.
- [ ] Wiring, polaritas sensor, emergency stop, dan anti-crush diverifikasi teknisi.
- [ ] Prosedur manual tersedia di lokasi.
