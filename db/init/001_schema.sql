CREATE TABLE IF NOT EXISTS topic_candidates (
  id UUID PRIMARY KEY,
  canonical_url TEXT NOT NULL UNIQUE,
  title VARCHAR(500) NOT NULL,
  creator VARCHAR(250),
  published_at TIMESTAMPTZ NOT NULL,
  viral_score NUMERIC(5,2) NOT NULL,
  state VARCHAR(50) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS source_items (
  id UUID PRIMARY KEY,
  topic_candidate_id UUID NOT NULL REFERENCES topic_candidates(id),
  url TEXT NOT NULL,
  publisher VARCHAR(250) NOT NULL,
  excerpt TEXT,
  reliability_score INTEGER NOT NULL CHECK (reliability_score BETWEEN 0 AND 100),
  captured_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS productions (
  id UUID PRIMARY KEY,
  topic_candidate_id UUID NOT NULL REFERENCES topic_candidates(id),
  state VARCHAR(50) NOT NULL,
  version INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS script_versions (
  id UUID PRIMARY KEY,
  production_id UUID NOT NULL REFERENCES productions(id),
  version INTEGER NOT NULL,
  body TEXT NOT NULL,
  claim_map_json JSONB NOT NULL,
  created_at TIMESTAMPTZ NOT NULL,
  UNIQUE (production_id, version)
);

CREATE TABLE IF NOT EXISTS assets (
  id UUID PRIMARY KEY,
  object_key TEXT NOT NULL,
  type VARCHAR(50) NOT NULL,
  source_url TEXT,
  rights_status VARCHAR(50) NOT NULL,
  license_evidence TEXT,
  checksum VARCHAR(128) UNIQUE
);

CREATE TABLE IF NOT EXISTS asset_usages (
  id UUID PRIMARY KEY,
  production_id UUID NOT NULL REFERENCES productions(id),
  asset_id UUID NOT NULL REFERENCES assets(id),
  in_milliseconds INTEGER NOT NULL CHECK (in_milliseconds >= 0),
  out_milliseconds INTEGER NOT NULL CHECK (out_milliseconds > in_milliseconds),
  narrative_purpose TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS review_decisions (
  id UUID PRIMARY KEY,
  production_id UUID NOT NULL REFERENCES productions(id),
  kind VARCHAR(50) NOT NULL,
  decision VARCHAR(50) NOT NULL,
  reviewer_id VARCHAR(250) NOT NULL,
  notes TEXT,
  decided_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_events (
  id BIGSERIAL PRIMARY KEY,
  actor VARCHAR(250) NOT NULL,
  action VARCHAR(100) NOT NULL,
  entity_type VARCHAR(100) NOT NULL,
  entity_id VARCHAR(100) NOT NULL,
  payload_json JSONB,
  occurred_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_source_items_candidate ON source_items(topic_candidate_id);
CREATE INDEX IF NOT EXISTS ix_productions_candidate ON productions(topic_candidate_id);
CREATE INDEX IF NOT EXISTS ix_audit_events_entity ON audit_events(entity_type, entity_id, occurred_at DESC);
