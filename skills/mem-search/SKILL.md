---
name: mem-search
description: Search your project memory for past observations, decisions, and context. Use when you need to recall what was done in previous sessions, find specific code changes, or understand project history.
---

# Memory Search Skill

Search your persistent memory across sessions using hybrid search (FTS5 + vector).

## 3-Layer Workflow (Token Efficient)

1. **Search** → Get compact index with IDs (~50-100 tokens/result)
2. **Timeline** → Get chronological context around interesting results
3. **Get Details** → Fetch full observations ONLY for filtered IDs

## API Endpoints

Base URL: `http://127.0.0.1:37777`

### 1. Search Index

```bash
curl "http://127.0.0.1:37777/api/search?query=YOUR_QUERY&limit=10"
```

Parameters:
- `query` (required): Search text
- `limit`: Max results (default: 20)
- `type`: Filter by type (discovery, modification, action, observation)
- `project`: Filter by project name
- `dateStart`: Unix timestamp (ms) for date range start
- `dateEnd`: Unix timestamp (ms) for date range end

Returns compact index with IDs, titles, types, and relevance scores.

### 2. Timeline Context

```bash
curl "http://127.0.0.1:37777/api/timeline?anchor=OBSERVATION_ID&depthBefore=3&depthAfter=3"
```

Or search for an anchor:
```bash
curl "http://127.0.0.1:37777/api/timeline?query=YOUR_QUERY"
```

Parameters:
- `anchor`: Observation ID to center on
- `query`: Search for anchor observation
- `depthBefore`: Number of observations before (default: 3)
- `depthAfter`: Number of observations after (default: 3)

### 3. Get Full Observations

```bash
curl -X POST "http://127.0.0.1:37777/api/observations/batch" \
  -H "Content-Type: application/json" \
  -d '{"ids": [123, 456, 789]}'
```

Always batch multiple IDs in a single request for efficiency.

## Example Workflow

```bash
# Step 1: Search for authentication-related observations
curl "http://127.0.0.1:37777/api/search?query=authentication%20bug&limit=10"
# Returns: [{ id: 123, title: "Fixed auth...", score: 0.85 }, ...]

# Step 2: Get timeline around interesting result
curl "http://127.0.0.1:37777/api/timeline?anchor=123"
# Returns: { before: [...], anchor: {...}, after: [...] }

# Step 3: Fetch full details for relevant IDs
curl -X POST "http://127.0.0.1:37777/api/observations/batch" \
  -H "Content-Type: application/json" \
  -d '{"ids": [123, 125]}'
# Returns full observation details
```

## Search Status

Check if memory search is available:

```bash
curl "http://127.0.0.1:37777/api/search/status"
```

Returns search mode (hybrid, fts5-only, vector-only) and availability.

## Tips

- Start with broad searches, then narrow down
- Use timeline to understand context around events
- Only fetch full details for IDs you actually need
- Filter by type to focus on specific kinds of observations
- Use date ranges to limit to recent work
