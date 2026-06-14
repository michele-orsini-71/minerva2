-- Embedding model and dimension fold into the metadata bag's
-- provenance.invariants section; the dedicated columns are no longer the
-- source of truth.
ALTER TABLE collections DROP COLUMN embedding_model;
ALTER TABLE collections DROP COLUMN embedding_dimension;
