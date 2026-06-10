# ADR-003: PostgreSQL with pg_trgm and LIST partitioning as word store

**Status:** Accepted  
**Date:** 2026-06-08

## Context

The original VocabVersus used **Lucene.NET** (via the `vocabversus-wordset-evaluator` service) to store and search wordlists. This ran into two problems:

1. **Dual-query conflict**: Lucene was asked to serve two structurally different queries — (a) "does this exact word exist in the wordlist?" for real-time validation, and (b) "what character combinations are guaranteed solvable?" for round generation. Lucene's inverted index optimises for text search, not set-membership or combinatorial derivation.
2. **Editability**: Modifying a Lucene index (adding/removing words from community-created lists) requires careful index management and a restart/reopen cycle.

The new word store must support:
- **Fast exact-match lookup**: "Is this word in wordlist X?" — primary hot path during gameplay
- **Fuzzy matching** (nice-to-have): 1-2 character typo tolerance
- **Letter-combination derivation**: given a wordlist, enumerate which character sets (1-3 chars) have at least one valid answer
- **Editability**: wordlists can be created, extended, and trimmed by users
- **Scale**: individual wordlists can be very large (e.g. a full English dictionary may contain 100k+ words); the system hosts many wordlists simultaneously
- **Compact storage**: prefer storage-efficient representations over denormalised caching

## Decision

Use **PostgreSQL** with the `pg_trgm` extension and **LIST partitioning by `wordset_id`**.

### Schema

```sql
CREATE TABLE wordsets (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,             -- NOT unique; multiple wordsets can share a name
    share_code  CHAR(6) NOT NULL UNIQUE,   -- e.g. 'XK7P2M'; human-readable sharing identifier
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Parent partitioned table — never queried directly
CREATE TABLE words (
    wordset_id      UUID NOT NULL REFERENCES wordsets(id),
    word            TEXT NOT NULL,       -- stored lowercase
    frequency_rank  INT,                 -- optional; lower = more common
    PRIMARY KEY (wordset_id, word)       -- no separate id column
) PARTITION BY LIST (wordset_id);

-- Example partition (created dynamically when a wordset is created):
-- CREATE TABLE words_<wordset_uuid> PARTITION OF words
--     FOR VALUES IN ('<wordset_uuid>');
-- CREATE INDEX ON words_<wordset_uuid> USING gist (word gist_trgm_ops);
```

### Why partitioning?

Without partitioning, a single `words` table hosting a 150k-word English dictionary alongside dozens of community lists means every index scan touches entries from all wordlists. With LIST partitioning by `wordset_id`, each wordset's words are physically co-located in their own sub-table, with their own indexes. A lookup against wordset A never touches wordset B's pages. Dropping a wordset is a single `DROP TABLE` on the partition — no expensive `DELETE` scan.

### Fuzzy matching

The `pg_trgm` extension provides trigram-based similarity. A GiST index on the `word` column of each partition enables queries like:

```sql
SELECT word FROM words_<id> WHERE word % 'submittd' ORDER BY similarity(word, 'submittd') DESC LIMIT 1;
```

This supports 1-2 character typo forgiveness without a separate search engine.

`pg_trgm` measures trigram overlap rather than Levenshtein edit distance. For cases where edit-distance precision matters more (e.g. short words where transpositions are common), PostgreSQL's `fuzzystrmatch` extension provides a native `levenshtein()` function:

```sql
SELECT word FROM words_<id> ORDER BY levenshtein(word, 'submittd') ASC LIMIT 1;
```

`levenshtein()` cannot use the GiST index (full scan), so it is reserved for the fuzzy-only code path. The exact-match hot path always uses the indexed lookup.

### Letter-combination derivation

A query over a wordset's partition can enumerate all distinct character subsets (1-3 chars) that appear as substrings of at least one word. This is computed at wordset creation and on-demand, not stored redundantly.

### frequency_rank

Populated at import time from an external frequency corpus (e.g. Google Ngrams, word frequency lists). `NULL` means frequency is unknown. The game engine uses this at round-end to calculate rarity bonuses — it is never written during live gameplay.

## Consequences

**Positive:**
- Exact-match lookup is an indexed primary-key lookup — O(log n) per partition
- Large wordlists are isolated; adding a 150k-word list doesn't degrade lookup performance for a 50-word list
- Editable without re-indexing: `INSERT`/`DELETE` on a partition updates the index incrementally
- Fuzzy matching is built-in via `pg_trgm` — no additional service needed
- PostgreSQL is a well-understood operational dependency; one less infrastructure component vs. adding Elasticsearch

**Negative:**
- Dynamic partition creation requires DDL at runtime (the wordset-service executes `CREATE TABLE ... PARTITION OF ...`). This is non-standard EF Core usage and requires raw SQL or a migration helper
- Partition count grows with wordset count — PostgreSQL handles thousands of partitions fine, but this is worth monitoring if the platform grows very large
- `pg_trgm` similarity is trigram-based, not pure Levenshtein edit distance — for short words with transpositions, it is less precise than Lucene's `FuzzyQuery`. Mitigated by the `fuzzystrmatch` `levenshtein()` fallback.
- `pg_trgm` is language-unaware; it may return odd suggestions for non-Latin scripts

## Alternatives Considered

- **Lucene.NET (original)**: Rejected — proven problematic for dual-query use case and editability. Letter-combination derivation is not natively expressible in Lucene (requires fetching all words into application memory). All wordsets share one index, requiring a wordset-ID filter on every query and no partition isolation. The library (`4.8.0-beta00016`) remains in beta with slow release cadence. Fuzzy matching via `FuzzyQuery` uses Levenshtein edit distance, which is more precise than `pg_trgm` for typo correction, but this advantage is recoverable via PostgreSQL's `fuzzystrmatch` extension without the other trade-offs.
- **Redis sets**: Exact-match is O(1) via `SISMEMBER`, but no built-in fuzzy matching and limited query capability for letter-combo derivation. Storage efficiency is worse than PostgreSQL for large text sets.
- **Elasticsearch / OpenSearch**: Excellent fuzzy search, but adds significant infrastructure complexity (JVM, separate cluster), is harder to edit atomically, and is overkill when PostgreSQL's `pg_trgm` covers the fuzzy-matching requirement adequately
- **Flat file / in-memory HashSet**: Fast lookups but not persistent or editable without service restarts
