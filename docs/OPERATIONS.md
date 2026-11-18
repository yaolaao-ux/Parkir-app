# Operasional, Monitoring, dan Recovery

Dokumen ini melengkapi README. Jangan menyimpan password atau connection string di file ini.

## 1. Deploy

1. Backup database (lihat bagian 3).
2. Deploy API baru. Startup menjalankan migration yang belum tercatat di `schema_migrations`.
3. Cek `GET /health` sampai `200`.
4. Terbitkan kredensial perangkat baru melalui `POST /api/admin/devices/{deviceId}/credential`.
5. Deploy POS dan firmware dengan `X-Device-Id`/`PARKIR_DEVICE_ID` serta key masing-masing.
6. Uji satu entry dan satu checkout di staging sebelum membuka gate produksi.

Jangan menjalankan migration terhadap production tanpa backup dan persetujuan pemilik sistem.

## 2. Monitoring minimum

- `GET /health` harus `200`; `503` berarti database tidak siap.
- Log API memakai `X-Correlation-ID`; gunakan ID tersebut untuk menelusuri request.
- Alarm yang disarankan:
  - perangkat tidak mengirim heartbeat lebih dari 60 detik;
  - antrean checkout POS berstatus `FAILED` atau menumpuk;
  - command gate berstatus `SENT`/`ACKNOWLEDGED` terlalu lama;
  - error rate API 5xx meningkat;
  - backup gagal atau ukuran backup turun drastis.
- Jangan log token, password, API key, isi payload pembayaran, atau data pribadi.

## 3. Backup

```sh
DATABASE_URL='postgresql://user:pass@db-host:5432/parkir' \
BACKUP_DIR=/var/backups/parkir \
BACKUP_RETENTION_DAYS=30 \
bash tools/backup-postgres.sh
```

Simpan salinan di lokasi terpisah, enkripsi jika berisi data pribadi, dan catat checksum. Backup belum dianggap berhasil sebelum restore diuji.

## 4. Restore drill

Restore hanya ke database terpisah:

```sh
RESTORE_CONFIRM=YES \
DATABASE_URL='postgresql://user:pass@db-host:5432/parkir_restore' \
bash tools/restore-postgres.sh backups/parkir-YYYYMMDDTHHMMSSZ.dump
```

Setelah restore, verifikasi jumlah transaksi, relasi member/transaksi, sequence, index, dan total revenue terhadap sumber. Catat RPO/RTO aktual.

## 5. POS offline dan antrean

- Quote dan tarif wajib berasal dari server. POS tidak boleh melakukan pembayaran final offline.
- Checkout yang sudah masuk outbox hanya dianggap selesai setelah server mengonfirmasi.
- Jika server offline, jangan membuka palang secara manual kecuali prosedur darurat disetujui.
- Item `FAILED` harus ditinjau dan di-retry setelah penyebabnya diperbaiki.
- File lokal: `%LOCALAPPDATA%/Parkir/Pos/checkout-outbox.db` dan `outbox.log`.
- Jangan menyalin database outbox dari POS lain; idempotency key harus tetap pada perangkat asal.

## 6. Gate dan printer

- `PRINT_SENT` bukan bukti kertas tercetak. Jika printer tidak memiliki status fisik, operator harus memeriksa hasil cetak.
- Command gate `EXECUTED` berarti perintah relay terkirim, bukan bukti posisi palang.
- Jika relay/printer gagal, gunakan prosedur manual dengan audit; jangan mengulang command tanpa memeriksa kendaraan.
- Keselamatan tetap bergantung pada hardware: emergency stop, sensor anti-crush, dan interlock harus diuji teknisi.

## 7. Recovery singkat

| Gejala | Tindakan |
|---|---|
| `/health` 503 | Periksa database, network, kredensial; jangan bypass health check |
| POS tidak bisa login | Periksa API, akun aktif, dan `PARKIR_DEVICE_KEY` |
| POS heartbeat gagal | Periksa kredensial perangkat, `X-Device-Id`, gate registry |
| Checkout `PENDING`/`SENT` | Jangan buka palang; tunggu konfirmasi atau retry aman |
| Checkout `FAILED` | Tinjau error, perbaiki penyebab, retry; jangan hapus tanpa audit |
| Gate command `FAILED` | Buka manual sesuai prosedur dan catat kejadian |
| Migration gagal | Jangan hapus data; restore backup atau selesaikan preflight |
