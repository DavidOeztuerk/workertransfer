"""Smoke tests for worker-search (Phase 1.5).

Exercises the search DTOs only — ``SearchQuery`` and ``SearchResult``. The
concrete engines (Elasticsearch/Meilisearch/Vector) are lazily imported inside
their ``__init__`` and build live clients pointing at localhost — not touched.
"""

from worker_search import SearchQuery, SearchResult


def test_smoke_search_dtos() -> None:
    query = SearchQuery(query="hello")

    assert query.query == "hello"
    assert query.limit == 10
    assert query.offset == 0

    result = SearchResult(id="1", score=0.5, document={"x": 1})

    assert result.id == "1"
    assert result.score == 0.5
