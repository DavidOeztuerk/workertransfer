"""Search abstraction: Elasticsearch, Meilisearch, Vector search."""

from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Any, cast


@dataclass
class SearchResult:
    id: str
    score: float
    document: dict[str, Any]
    highlights: dict[str, list[str]] | None = None


@dataclass
class SearchQuery:
    query: str
    filters: dict[str, Any] | None = None
    limit: int = 10
    offset: int = 0
    sort: list[tuple[str, str]] | None = None


class SearchEngine(ABC):
    @abstractmethod
    async def index(self, index: str, document: dict[str, Any], id: str | None = None) -> str: ...

    @abstractmethod
    async def bulk_index(self, index: str, documents: list[dict[str, Any]]) -> int: ...

    @abstractmethod
    async def search(self, index: str, query: SearchQuery) -> list[SearchResult]: ...

    @abstractmethod
    async def delete(self, index: str, id: str) -> bool: ...

    @abstractmethod
    async def create_index(self, index: str, mapping: dict[str, Any]) -> bool: ...


class ElasticsearchEngine(SearchEngine):
    def __init__(self, hosts: list[str] | None = None):
        from elasticsearch import AsyncElasticsearch

        self._client = AsyncElasticsearch(hosts=hosts or ["http://localhost:9200"])

    async def index(self, index: str, document: dict[str, Any], id: str | None = None) -> str:
        result = await self._client.index(index=index, document=document, id=id)
        return str(cast("dict[str, Any]", result)["_id"])

    async def bulk_index(self, index: str, documents: list[dict[str, Any]]) -> int:
        from elasticsearch.helpers import async_bulk

        actions: list[dict[str, Any]] = [{"_index": index, "_source": doc} for doc in documents]
        result = await async_bulk(self._client, actions)
        return int(cast("tuple[Any, ...]", result)[0])

    async def search(self, index: str, query: SearchQuery) -> list[SearchResult]:
        body = {
            "query": {"query_string": {"query": query.query}},
            "size": query.limit,
            "from": query.offset,
        }
        if query.filters:
            body["query"] = {
                "bool": {
                    "must": [{"query_string": {"query": query.query}}],
                    "filter": query.filters,
                }
            }
        if query.sort:
            body["sort"] = [{field: order} for field, order in query.sort]

        result = await self._client.search(index=index, body=body)
        hits = cast("dict[str, Any]", cast("dict[str, Any]", result)["hits"])["hits"]
        return [
            SearchResult(
                id=cast("str", hit["_id"]),
                score=float(cast("dict[str, Any]", hit)["_score"] or 0.0),
                document=cast("dict[str, Any]", hit)["_source"],
                highlights=cast(
                    "dict[str, list[str]] | None", cast("dict[str, Any]", hit).get("highlight")
                ),
            )
            for hit in hits
        ]

    async def delete(self, index: str, id: str) -> bool:
        try:
            await self._client.delete(index=index, id=id)
            return True
        except Exception:
            return False

    async def create_index(self, index: str, mapping: dict[str, Any]) -> bool:
        if not await self._client.indices.exists(index=index):
            await self._client.indices.create(index=index, mappings=mapping)
        return True


class MeilisearchEngine(SearchEngine):
    def __init__(self, url: str = "http://localhost:7700", api_key: str | None = None):
        import meilisearch

        self._client = meilisearch.Client(url, api_key)

    async def index(self, index: str, document: dict[str, Any], id: str | None = None) -> str:
        idx = cast("Any", self._client.index(index))
        if id:
            document["id"] = id
        task = await idx.add_documents([document])
        return str(task.task_uid)

    async def bulk_index(self, index: str, documents: list[dict[str, Any]]) -> int:
        idx = cast("Any", self._client.index(index))
        task = await idx.add_documents(documents)
        return int(task.task_uid)

    async def search(self, index: str, query: SearchQuery) -> list[SearchResult]:
        idx = cast("Any", self._client.index(index))
        result = cast(
            "dict[str, Any]",
            await idx.search(
                query.query,
                {
                    "limit": query.limit,
                    "offset": query.offset,
                    "filter": query.filters,
                    "sort": query.sort,
                },
            ),
        )
        hits = cast("list[dict[str, Any]]", result.get("hits", []))
        return [
            SearchResult(
                id=cast("str", hit["id"]),
                score=float(hit.get("_rankingScore", 0)),
                document=hit,
            )
            for hit in hits
        ]

    async def delete(self, index: str, id: str) -> bool:
        idx = cast("Any", self._client.index(index))
        await idx.delete_document(id)
        return True

    async def create_index(self, index: str, mapping: dict[str, Any]) -> bool:
        try:
            await cast("Any", self._client).create_index(index, {"primaryKey": "id"})
        except Exception:
            pass
        return True


class VectorSearchEngine(SearchEngine):
    def __init__(self, url: str = "http://localhost:6333"):
        from qdrant_client import AsyncQdrantClient

        self._client = AsyncQdrantClient(url=url)

    async def index(self, index: str, document: dict[str, Any], id: str | None = None) -> str:
        from qdrant_client.models import PointStruct

        vector = document.pop("vector", [])
        point = PointStruct(id=id or document.get("id", ""), vector=vector, payload=document)
        await self._client.upsert(collection_name=index, points=[point])
        return str(point.id)

    async def bulk_index(self, index: str, documents: list[dict[str, Any]]) -> int:
        from qdrant_client.models import PointStruct

        points = [
            PointStruct(id=doc.get("id", ""), vector=doc.pop("vector", []), payload=doc)
            for doc in documents
        ]
        await self._client.upsert(collection_name=index, points=points)
        return len(points)

    async def search(self, index: str, query: SearchQuery) -> list[SearchResult]:
        from qdrant_client.models import Filter

        vector = query.query  # Assume query is the vector
        filters = Filter(**query.filters) if query.filters else None
        result = await cast(
            "Any",
            self._client,
        ).search(
            collection_name=index,
            query_vector=vector,
            limit=query.limit,
            offset=query.offset,
            query_filter=filters,
        )
        return [
            SearchResult(id=str(hit.id), score=float(hit.score), document=hit.payload)
            for hit in result
        ]

    async def delete(self, index: str, id: str) -> bool:
        await self._client.delete(collection_name=index, points_selector=[id])
        return True

    async def create_index(self, index: str, mapping: dict[str, Any]) -> bool:
        from qdrant_client.models import Distance, VectorParams

        try:
            await self._client.create_collection(
                collection_name=index,
                vectors_config=VectorParams(
                    size=int(mapping.get("dim", 768)), distance=Distance.COSINE
                ),
            )
        except Exception:
            pass
        return True
