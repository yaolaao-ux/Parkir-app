# Pilot Readiness Checklist

Status dokumen: **BELUM SIAP PILOT** sampai seluruh item wajib terpenuhi dan ditandatangani.

## 1. Kode dan data

- [ ] Branch/commit yang akan dipakai dicatat.
- [ ] Build API dan POS dari commit tersebut berhasil tanpa warning.
- [ ] Regression tests lulus.
- [ ] Database smoke/migration lulus pada staging.
- [ ] Backup database terbaru tersedia dan checksum tercatat.
- [ ] Restore drill berhasil ke database terpisah.
- [ ] Tidak ada secret aktif di repository/artifact.

## 2. Keamanan

- [ ] Kredensial perangkat unik diterbitkan untuk setiap gate/POS.
- [ ] Kredensial lama/bersama sudah dicabut.
- [ ] User demo/tidak terpakai dinonaktifkan atau password-nya dirotasi.
- [ ] TLS/reverse proxy staging diuji.
- [ ] Rate limit login/perangkat diuji.
- [ ] Akses admin/kasir diuji lintas role.
- [ ] Device binding/revocation diuji.

## 3. Transaksi

- [ ] Entry retry tidak membuat tiket ganda.
- [ ] Checkout retry tidak membuat pembayaran ganda.
- [ ] Perubahan tarif tidak mengubah sesi aktif.
- [ ] Max daily, grace, lost ticket, dan pergantian hari diuji.
- [ ] Member valid/invalid/expired diuji.
- [ ] Void dan reprint memiliki audit.
- [ ] Dua kasir tidak dapat menyelesaikan sesi yang sama.

## 4. Gate dan peripheral

- [ ] `docs/HARDWARE_TEST_PLAN.md` seluruh skenario P0 lulus.
- [ ] Emergency stop, safety edge, anti-crush, dan sensor stuck diuji teknisi.
- [ ] Gate-In print/reconnect/idempotency diuji.
- [ ] Gate-Out relay/command/status diuji.
- [ ] Printer offline/kertas habis/cover terbuka diuji sesuai kemampuan.
- [ ] Prosedur manual tersedia dan dilatih.

## 5. Operasional

- [ ] `docs/OPERATIONS.md` tersedia di lokasi.
- [ ] Operator tahu arti `PENDING`/`SENT`/`FAILED`/`HARDWARE_DONE`.
- [ ] Operator tahu larangan membuka palang sebelum konfirmasi server.
- [ ] Kontak eskalasi dan jadwal on-call tersedia.
- [ ] Monitoring/alert perangkat offline dan queue gagal diuji.
- [ ] Rollback aplikasi dan database diuji di staging.
- [ ] RPO/RTO disetujui.

## 6. Go/No-Go

| Area | Penanggung jawab | Status | Catatan |
|---|---|---|---|
| Keamanan |  |  |  |
| Database/backup |  |  |  |
| API/POS |  |  |  |
| Gate-In |  |  |  |
| Gate-Out |  |  |  |
| Keselamatan fisik |  |  |  |
| Operasional |  |  |  |

**Keputusan pilot:** ☐ GO ☐ NO-GO

Pilot hanya boleh dimulai jika seluruh item P0 berstatus lulus dan teknisi hardware menyetujui keselamatan. Agent/software tidak berwenang menyatakan production approved.
