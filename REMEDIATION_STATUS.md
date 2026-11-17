# Remediation Status

## Gelombang

Gelombang 0 — Baseline dan secret hygiene.

## Baseline

- Repository: `/root/Parkir-app`
- Branch kerja: `chore/remediation-baseline`
- Commit awal: `508b805` (`Harden POS checkout and backend operations`)
- Commit akhir: branch head setelah commit dokumen Gelombang 0 (lihat `git log -1 --oneline`)
- Working tree saat baseline: bersih
- Dokumen acuan: `PARKIR_APP_REMEDIATION_PLAN.md` diberikan melalui prompt; file tersebut belum ada di repository saat baseline
- SDK: .NET 8.0.428 (linux-arm64)
- Node: v22.22.1
- PostgreSQL client: 18.6; server database lokal tidak berjalan pada `127.0.0.1:5432`
- PlatformIO: tidak terpasang pada environment ini

### Inventaris singkat

- API: `backend/Parkir.Api` (ASP.NET Core .NET 8)
- POS: `pos/Parkir.Pos` (WPF, target Windows)
- Dashboard: `backend/Parkir.Api/wwwroot`
- Database: PostgreSQL; 7 tabel (`vehicle_types`, `members`, `users`, `audit_logs`, `devices`, `gates`, `transactions`)
- Migration: `database/migrations/002`–`009`; dijalankan otomatis oleh `DatabaseInitializer`
- Firmware: `firmware/gate_in/gate_in.ino` + `platformio.ini`
- Test: `tests/Parkir.Tests` (runner konsol)
- CI: `.github/workflows/build-pos.yml`, `docker.yml`, `tests.yml`, dan `security.yml` (baru)

## Temuan yang diverifikasi

| ID | Status | Bukti ringkas |
|---|---|---|
| R-01 — kredensial demo | **CONFIRMED, partial** | README current tree sudah bersih dari password demo. Namun riwayat Git masih memiliki penanda `Password:` pada beberapa commit lama (`afec37f`, `be337ea`, `91b0203`, `80c01a3`, dan commit sebelumnya). Nilai tidak ditampilkan. |
| R-02 — fallback password DB | **CONFIRMED** | `backend/Parkir.Api/Program.cs` masih memakai connection string fallback dengan kredensial default bila konfigurasi tidak tersedia. Dijadwalkan Gelombang 2. |
| R-03 — port PostgreSQL terbuka | **CONFIRMED** | `docker-compose.yml` masih memetakan `5432:5432`. Dijadwalkan Gelombang 2. |
| R-04 — tarif tidak disnapshot | **FIXED di 508b805** | Kolom snapshot tarif ada di `transactions`; `ParkingRepository` memakai `COALESCE` snapshot; test 24/25 jam lulus. |
| R-05 — entry tidak idempotent | **FIXED di 508b805** | `EntryRequest.IdempotencyKey`, unique index per gate, dan pengembalian tiket lama; test kontrak JSON lulus. Integration DB belum dijalankan. |
| R-06 — concurrency checkout | **PARTIAL** | Update checkout memakai `WHERE payment_status = 'UNPAID'` dan idempotency key; belum ada integration test dua kasir bersamaan dengan PostgreSQL nyata. |
| R-07 — audit non-atomic | **PARTIAL** | Checkout dan void menulis audit dalam transaksi yang sama. Mutasi admin (tarif/member/gate) masih menulis audit melalui repository terpisah. |
| R-08 — anti-passback | **CONFIRMED belum ada** | Belum ada constraint/pemeriksaan sesi aktif ganda per member/kendaraan. Dijadwalkan Gelombang 1/5. |
| R-09 — entropi tiket rendah | **CONFIRMED** | Ticket ID memotong GUID menjadi 8 karakter hex (32 bit) melalui `[..21]` di `ParkingService`. Dijadwalkan Gelombang 1. |
| R-10/R-12 — API key perangkat bersama | **CONFIRMED** | Endpoint perangkat memakai satu `X-API-Key` global; belum ada kredensial unik/revocation per device. Dijadwalkan Gelombang 2. |
| R-11 — session lifecycle | **PARTIAL** | JWT 8 jam, rate limit login, dan POS re-login sudah ada; belum ada revocation/disable user end-to-end test. |
| R-13 — printer/ticket state | **PARTIAL** | Firmware mempertahankan idempotency key sampai print sukses; status `PRINT_CONFIRMED` fisik belum tersedia. Dijadwalkan Gelombang 3. |
| R-17 — offline queue | **PARTIAL** | Status outbox, backoff, dead-letter, dan tombol tindakan sudah ada; rekonsiliasi manual penuh dan test DB queue belum ada. |
| R-19 — timezone | **PARTIAL** | `BusinessClock` dengan `Business:TimeZone` default `Asia/Jakarta`; lokasi operasional belum dipastikan. Dijadwalkan Gelombang 5. |
| R-20 — automated tests | **PARTIAL** | 21 regression test lulus; integration/E2E/hardware tests belum tersedia. |

Temuan yang tidak lagi relevan pada commit baseline:

- Password demo di README current tree sudah dihapus.
- Trailing-slash API key bypass sudah ditutup dan diuji.
- Timezone report/dashboard tidak lagi memakai `CURRENT_DATE`/UTC C#.
- POS outbox tidak lagi memblokir antrean pada 4xx pertama.

Temuan yang belum dapat diverifikasi:

- Migration runner terhadap PostgreSQL nyata.
- Perilaku WPF pada Windows.
- Perilaku firmware pada PlatformIO/hardware.
- Backup/restore dan monitoring produksi.

## Perubahan yang dilakukan

| File | Ringkasan |
|---|---|
| `.gitignore` | Menambahkan pola file secret/private key, database lokal, dan file sensitif. |
| `tools/scan-secrets.sh` | Pemindai secret dependency-free untuk tracked files; hanya mencetak nama file, bukan nilai. |
| `.github/workflows/security.yml` | Menjalankan secret scan pada push, pull request, dan manual. |
| `REMEDIATION_STATUS.md` | Dokumen status Gelombang 0 ini. |

Tidak ada perubahan source code API/POS/firmware, migration, atau data pada Gelombang 0.

## Database

- Migration: tidak ada migration baru pada Gelombang 0.
- Dampak data: tidak ada.
- Cara menjalankan: tidak applicable.
- Cara rollback/pemulihan: tidak applicable.

## Pengujian

| Test | Perintah | Hasil |
|---|---|---|
| Build API Release | `dotnet build backend/Parkir.Api/Parkir.Api.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Build POS WPF Release | `dotnet build pos/Parkir.Pos/Parkir.Pos.csproj --configuration Release` | **PASS** — 0 warning, 0 error |
| Regression tests | `dotnet run --project tests/Parkir.Tests/Parkir.Tests.csproj --configuration Release` | **PASS** — 21/21 |
| Secret scan current tree | `bash tools/scan-secrets.sh` | **PASS** — tidak ada pola secret berisiko tinggi pada tracked files |
| Secret scan riwayat Git | pemeriksaan penanda `Password:` pada `README.md` historis | **FAIL/BLOCKED** — kredensial lama terdapat di riwayat; perlu keputusan pemilik |
| Integration tests PostgreSQL | belum dijalankan | **NOT RUN** — server database tidak berjalan |
| WPF runtime/visual | belum dijalankan | **NOT RUN** — environment Linux |
| Firmware build | `pio run` | **BLOCKED** — PlatformIO tidak terpasang |

## Acceptance criteria Gelombang 0

| Kriteria | Status |
|---|---|
| Tidak ada secret aktif baru di current tree | **PASS** — pemindaian pola high-signal dan review README |
| Secret scanning tersedia di CI | **PASS** — workflow dan script ditambahkan |
| Kredensial yang pernah terekspos dirotasi | **BLOCKED** — membutuhkan akses database/layanan dan persetujuan pemilik |
| Riwayat Git dibersihkan dari kredensial lama | **BLOCKED** — force push/rewrite history dilarang tanpa persetujuan eksplisit |
| Baseline build/test tercatat | **PASS** |
| Working tree bersih sebelum perubahan | **PASS** |
| Konfigurasi development tidak terganggu | **PASS** — hanya file ignore/CI/dokumentasi yang berubah |

## Risiko tersisa

1. **R-01 (P0):** password demo lama masih ada di riwayat Git. Rotasi password dan pembersihan history memerlukan koordinasi pemilik repository.
2. **R-02 (P0):** fallback connection string default masih ada; aplikasi production harus gagal start tanpa secret. Akan dikerjakan Gelombang 2.
3. **R-03 (P0):** port PostgreSQL masih dipublikasikan di Compose. Akan dikerjakan Gelombang 2.
4. **R-08/R-09 (P0/P1):** anti-passback dan entropi ticket ID belum diperbaiki; Gelombang 1.
5. Integration test PostgreSQL, WPF runtime, dan hardware belum dijalankan; hasil build tidak boleh dianggap bukti siap operasional.

## Tindakan yang membutuhkan persetujuan

- Rotasi kredensial akun/database yang pernah memakai password demo.
- Pembersihan riwayat Git dan force push (dilarang pada prompt ini).
- Penyediaan database development/test PostgreSQL untuk integration test.
- Pengujian WPF pada Windows.
- Pengujian firmware dan keselamatan gate pada hardware.

## Rekomendasi gelombang berikutnya

Gelombang 1 — Transaction integrity: verifikasi dan selesaikan anti-passback, atomic audit untuk mutasi admin, penguatan entropi ticket ID, serta integration test PostgreSQL untuk entry/checkout idempotency dan concurrency.
