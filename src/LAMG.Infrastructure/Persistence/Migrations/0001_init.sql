-- =============================================================
-- 0001_init.sql
-- Initial schema for Longform Audio Mix Generator (v1).
--
-- Conventions:
--  * Timestamps are stored as INTEGER Unix milliseconds.
--  * Enums are stored as TEXT (their .NET member name).
--  * SQLite is run in WAL mode; PRAGMAs are applied by the
--    connection factory, not by this script.
-- =============================================================

-- -------------------------------------------------------------
-- Projects: top-level container for one run of the pipeline.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Projects (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    name          TEXT    NOT NULL,
    created_at    INTEGER NOT NULL,
    settings_json TEXT    NOT NULL,
    output_folder TEXT    NOT NULL
);

-- -------------------------------------------------------------
-- Batches: one imported folder. Unique-mode mixes are scoped
-- per batch; reuse-mode mixes draw from one or more selected
-- batches (via MixBatches).
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Batches (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id    INTEGER NOT NULL REFERENCES Projects(id) ON DELETE CASCADE,
    source_folder TEXT    NOT NULL,
    imported_at   INTEGER NOT NULL,
    track_count   INTEGER NOT NULL DEFAULT 0,
    UNIQUE (project_id, source_folder)
);

CREATE INDEX IF NOT EXISTS idx_batches_project ON Batches (project_id);

-- -------------------------------------------------------------
-- Tracks: one source audio file. Populated progressively by the
-- analysis pipeline.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Tracks (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    batch_id        INTEGER NOT NULL REFERENCES Batches(id) ON DELETE CASCADE,
    full_path       TEXT    NOT NULL,
    file_name       TEXT    NOT NULL,
    format          TEXT    NOT NULL,                     -- 'Mp3' | 'Wav'
    file_size_bytes INTEGER NOT NULL,
    duration_ms     INTEGER NOT NULL,
    sample_rate     INTEGER NOT NULL,
    channels        INTEGER NOT NULL,
    bitrate_kbps    INTEGER,
    file_hash       TEXT    NOT NULL,
    audio_hash      TEXT    NOT NULL,
    silence_lead_ms INTEGER NOT NULL DEFAULT 0,
    silence_tail_ms INTEGER NOT NULL DEFAULT 0,
    integrated_lufs REAL,
    true_peak_db    REAL,
    status          TEXT    NOT NULL,                     -- TrackStatus
    times_used      INTEGER NOT NULL DEFAULT 0,
    last_used_at    INTEGER
);

CREATE INDEX IF NOT EXISTS idx_tracks_batch      ON Tracks (batch_id);
CREATE INDEX IF NOT EXISTS idx_tracks_file_hash  ON Tracks (file_hash);
CREATE INDEX IF NOT EXISTS idx_tracks_audio_hash ON Tracks (audio_hash);
CREATE INDEX IF NOT EXISTS idx_tracks_file_name  ON Tracks (file_name);
CREATE INDEX IF NOT EXISTS idx_tracks_status     ON Tracks (status);

-- -------------------------------------------------------------
-- Mixes: one planned or rendered long-form mix.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Mixes (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id       INTEGER NOT NULL REFERENCES Projects(id) ON DELETE CASCADE,
    index_in_project INTEGER NOT NULL,
    target_min       INTEGER NOT NULL,                    -- 60 | 90 | 120
    actual_sec       INTEGER NOT NULL DEFAULT 0,
    mode             TEXT    NOT NULL,                    -- 'Unique' | 'Reuse'
    output_format    TEXT    NOT NULL,                    -- 'Mp3' | 'Wav'
    output_path      TEXT,
    created_at       INTEGER NOT NULL,
    status           TEXT    NOT NULL,                    -- MixStatus
    UNIQUE (project_id, index_in_project)
);

CREATE INDEX IF NOT EXISTS idx_mixes_project ON Mixes (project_id);
CREATE INDEX IF NOT EXISTS idx_mixes_status  ON Mixes (status);

-- -------------------------------------------------------------
-- MixItems: ordered tracks inside a mix, with the crossfade
-- durations chosen by the planner.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS MixItems (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    mix_id       INTEGER NOT NULL REFERENCES Mixes(id)  ON DELETE CASCADE,
    track_id     INTEGER NOT NULL REFERENCES Tracks(id),
    order_index  INTEGER NOT NULL,
    trimmed_ms   INTEGER NOT NULL,
    xfade_in_ms  INTEGER NOT NULL,
    xfade_out_ms INTEGER NOT NULL,
    UNIQUE (mix_id, order_index)
);

CREATE INDEX IF NOT EXISTS idx_mixitems_mix   ON MixItems (mix_id);
CREATE INDEX IF NOT EXISTS idx_mixitems_track ON MixItems (track_id);

-- -------------------------------------------------------------
-- MixBatches: many-to-many between mixes and the batches that
-- contributed tracks to them. Unique-mode mixes have exactly one
-- row; reuse-mode mixes have one row per selected batch.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS MixBatches (
    mix_id   INTEGER NOT NULL REFERENCES Mixes(id)   ON DELETE CASCADE,
    batch_id INTEGER NOT NULL REFERENCES Batches(id) ON DELETE CASCADE,
    PRIMARY KEY (mix_id, batch_id)
);

CREATE INDEX IF NOT EXISTS idx_mixbatches_batch ON MixBatches (batch_id);

-- -------------------------------------------------------------
-- Jobs: durable record of a long-running unit of work. Updated
-- on every state transition and heartbeat so the application
-- can resume after a crash.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Jobs (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id     INTEGER REFERENCES Projects(id),
    job_type       TEXT    NOT NULL,                       -- JobType
    status         TEXT    NOT NULL,                       -- JobStatus
    current_stage  TEXT    NOT NULL,                       -- JobStage
    last_heartbeat INTEGER NOT NULL,
    payload_json   TEXT    NOT NULL,
    created_at     INTEGER NOT NULL,
    finished_at    INTEGER
);

CREATE INDEX IF NOT EXISTS idx_jobs_status     ON Jobs (status);
CREATE INDEX IF NOT EXISTS idx_jobs_project    ON Jobs (project_id);
CREATE INDEX IF NOT EXISTS idx_jobs_heartbeat  ON Jobs (last_heartbeat);

-- -------------------------------------------------------------
-- Settings: keyed user / project settings.
--
-- project_id defaults to 0 (not NULL) so it can participate in a
-- composite PRIMARY KEY. The application treats 0 as "no project".
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Settings (
    key        TEXT    NOT NULL,
    value      TEXT    NOT NULL,
    scope      TEXT    NOT NULL,                            -- SettingScope
    project_id INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (key, scope, project_id)
);

-- -------------------------------------------------------------
-- LogEvents: only Warning+ is persisted; everything else goes to
-- the rolling file sink and the in-memory ring buffer.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS LogEvents (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at   INTEGER NOT NULL,
    level        TEXT    NOT NULL,
    source       TEXT    NOT NULL,
    message      TEXT    NOT NULL,
    exception    TEXT,
    context_json TEXT
);

CREATE INDEX IF NOT EXISTS idx_log_created ON LogEvents (created_at);
CREATE INDEX IF NOT EXISTS idx_log_level   ON LogEvents (level);
