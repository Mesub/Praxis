-- Runs once, on first boot of an empty data volume.

-- Embeddings for the Research/Content engines and for concept similarity.
CREATE EXTENSION IF NOT EXISTS vector;

-- Digest/hmac, for the Ledger's hash chain (M10.2).
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- A schema per engine. Modules never read each other's tables — the boundary is
-- enforced here as well as in the C# project graph, so that a stray query can't
-- quietly couple two engines together.
CREATE SCHEMA IF NOT EXISTS kernel;      -- the event log; the one thing everything else projects from
CREATE SCHEMA IF NOT EXISTS identity;    -- learners
CREATE SCHEMA IF NOT EXISTS graph;       -- E2  concepts, edges, PoM contracts
CREATE SCHEMA IF NOT EXISTS twin;        -- E1  concept states, misconceptions, calibration
CREATE SCHEMA IF NOT EXISTS compiler;    -- E3  plans
CREATE SCHEMA IF NOT EXISTS tutor;       -- E5  sessions, answer budgets
CREATE SCHEMA IF NOT EXISTS assessment;  -- E6  items, responses, rubrics
CREATE SCHEMA IF NOT EXISTS memory;      -- E7  rep schedule
CREATE SCHEMA IF NOT EXISTS reality;     -- E8  loops, predictions, resolutions
CREATE SCHEMA IF NOT EXISTS ledger;      -- E16 the signed evidence chain
