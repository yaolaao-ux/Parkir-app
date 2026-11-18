# Pemetaan Perangkat dan Gate

Isi tabel ini saat commissioning. Jangan menulis API key, password, atau private key di dokumen/repository.

| Gate ID | Tipe | Device ID | Peran | IP/MAC | Firmware | Kredensial dibuat | Terakhir diuji |
|---|---|---|---|---|---|---|---|
| GATE-IN-01 | GATE_IN | GATE-IN-01 | ESP32 Gate-In |  |  | [ ] |  |
| GATE-OUT-01 | GATE_OUT | GATE-OUT-01 | POS/relay |  |  | [ ] |  |
| GATE-IN-02 | GATE_IN | GATE-IN-02 | ESP32 Gate-In |  |  | [ ] |  |

## Aturan

- `Device ID` adalah identitas autentikasi perangkat; `Gate ID` adalah gate yang dipakai transaksi. POS memakai `PARKIR_DEVICE_ID` untuk header autentikasi dan `PARKIR_GATE_ID` untuk checkout/gate event. Keduanya boleh berbeda, tetapi harus sesuai data admin/device mapping.
- Kredensial diterbitkan per device melalui `POST /api/admin/devices/{deviceId}/credential`; key hanya ditampilkan sekali.
- Satu key tidak boleh dipakai beberapa perangkat.
- Perangkat yang diganti harus dicabut kredensial lamanya.
- Catat versi firmware dan waktu uji, bukan nilai kredensial.
- Gate-Out yang masih memakai relai serial POS harus dicatat sebagai Opsi A pada ADR-0001.

## Checklist commissioning

- [ ] Gate ID di NVS/perangkat benar.
- [ ] Device key dikirim via Serial `SET_DEVICE_KEY=...` dan tersimpan di NVS; tidak ada di source code.
- [ ] Gate terdaftar dan aktif di dashboard admin.
- [ ] Kredensial device diterbitkan dan diuji.
- [ ] Heartbeat terlihat online.
- [ ] Entry/checkout test di staging berhasil.
- [ ] Wiring, polaritas sensor, emergency stop, dan anti-crush diverifikasi teknisi.
- [ ] Prosedur manual tersedia di lokasi.
