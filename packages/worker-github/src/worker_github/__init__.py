"""GitHub Intelligence: OAuth, Repo Scanner, Skill Analyzer, OSS Reputation."""

from dataclasses import dataclass
from datetime import datetime
from typing import Any, ClassVar

import httpx
from github import Github


@dataclass
class GitHubProfile:
    username: str
    name: str | None
    bio: str | None
    location: str | None
    email: str | None
    public_repos: int
    followers: int
    following: int
    created_at: datetime
    avatar_url: str


@dataclass
class Repository:
    name: str
    full_name: str
    description: str | None
    language: str | None
    stars: int
    forks: int
    watchers: int
    topics: list[str]
    created_at: datetime
    updated_at: datetime
    pushed_at: datetime
    is_fork: bool
    is_private: bool
    size: int
    default_branch: str


@dataclass
class ContributionStats:
    total_commits: int
    total_prs: int
    total_issues: int
    total_reviews: int
    languages: dict[str, int]
    frameworks: dict[str, int]
    commit_frequency: dict[str, int]  # month -> count
    contribution_years: list[int]


class GitHubClient:
    def __init__(self, token: str | None = None):
        self._token = token
        self._client = Github(token) if token else Github()
        self._http = httpx.AsyncClient(
            headers={"Authorization": f"Bearer {token}"} if token else {},
            timeout=30.0,
        )

    async def get_user(self, username: str) -> GitHubProfile:
        user = self._client.get_user(username)
        return GitHubProfile(
            username=user.login,
            name=user.name,
            bio=user.bio,
            location=user.location,
            email=user.email,
            public_repos=user.public_repos,
            followers=user.followers,
            following=user.following,
            created_at=user.created_at,
            avatar_url=user.avatar_url,
        )

    async def get_repositories(self, username: str, limit: int = 100) -> list[Repository]:
        user = self._client.get_user(username)
        repos = []
        for repo in user.get_repos()[:limit]:
            repos.append(
                Repository(
                    name=repo.name,
                    full_name=repo.full_name,
                    description=repo.description,
                    language=repo.language,
                    stars=repo.stargazers_count,
                    forks=repo.forks_count,
                    watchers=repo.watchers_count,
                    topics=repo.get_topics(),
                    created_at=repo.created_at,
                    updated_at=repo.updated_at,
                    pushed_at=repo.pushed_at,
                    is_fork=repo.fork,
                    is_private=repo.private,
                    size=repo.size,
                    default_branch=repo.default_branch,
                )
            )
        return repos

    async def analyze_skills(self, username: str) -> ContributionStats:
        user = self._client.get_user(username)
        stats = ContributionStats(
            total_commits=0,
            total_prs=0,
            total_issues=0,
            total_reviews=0,
            languages={},
            frameworks={},
            commit_frequency={},
            contribution_years=[],
        )

        for repo in user.get_repos()[:50]:  # Limit to 50 repos
            if repo.fork:
                continue

            # Get languages
            try:
                langs = repo.get_languages()
                for lang, bytes_count in langs.items():
                    stats.languages[lang] = stats.languages.get(lang, 0) + bytes_count
            except Exception:
                pass

            # Get commits
            try:
                commits = repo.get_commits(author=user.login)
                stats.total_commits += commits.totalCount

                for commit in commits[:100]:  # Limit
                    month = commit.commit.author.date.strftime("%Y-%m")
                    stats.commit_frequency[month] = stats.commit_frequency.get(month, 0) + 1
                    year = commit.commit.author.date.year
                    if year not in stats.contribution_years:
                        stats.contribution_years.append(year)
            except Exception:
                pass

        stats.contribution_years.sort()
        return stats

    async def get_repository_details(self, owner: str, repo_name: str) -> dict[str, Any]:
        """Get detailed repository info including README, contributors, etc."""
        repo = self._client.get_repo(f"{owner}/{repo_name}")
        return {
            "readme": repo.get_readme().decoded_content.decode() if repo.get_readme() else None,
            "contributors": [c.login for c in repo.get_contributors()],
            "releases": [r.tag_name for r in repo.get_releases()],
            "branches": [b.name for b in repo.get_branches()],
            "pull_requests": repo.get_pulls(state="all").totalCount,
            "issues": repo.get_issues(state="all").totalCount,
        }


class SkillAnalyzer:
    FRAMEWORK_INDICATORS: ClassVar[dict[str, list[str]]] = {
        "python": ["django", "flask", "fastapi", "pytest", "sqlalchemy", "celery"],
        "javascript": ["react", "vue", "next", "express", "jest", "webpack"],
        "java": ["spring", "hibernate", "maven", "gradle", "junit"],
        "go": ["gin", "echo", "gorm", "cobra"],
        "rust": ["actix", "tokio", "serde", "diesel"],
    }

    @classmethod
    def analyze(cls, stats: ContributionStats) -> dict[str, float]:
        """Return skill scores for languages and frameworks."""
        scores = {}

        # Language scores based on bytes and commit frequency
        total_bytes = sum(stats.languages.values())
        for lang, bytes_count in stats.languages.items():
            base_score = bytes_count / total_bytes if total_bytes > 0 else 0

            # Boost for framework usage
            framework_boost = 0.0
            for fw in cls.FRAMEWORK_INDICATORS.get(lang.lower(), []):
                if fw in stats.frameworks:
                    framework_boost += 0.1

            scores[lang] = min(1.0, base_score + framework_boost)

        return scores


class OSSReputationCalculator:
    @classmethod
    def calculate(cls, profile: GitHubProfile, stats: ContributionStats) -> dict[str, Any]:
        """Calculate OSS reputation score with multiple dimensions."""
        dimensions = {
            "technical_expertise": cls._technical_expertise(stats),
            "architecture": cls._architecture(stats),
            "open_source": cls._open_source(profile, stats),
            "community": cls._community(profile, stats),
            "leadership": cls._leadership(stats),
            "documentation": cls._documentation(stats),
            "testing": cls._testing(stats),
            "devops": cls._devops(stats),
            "ai": cls._ai(stats),
            "security": cls._security(stats),
        }

        # Normalize to 0-100 scale
        normalized = {k: round(v * 100, 1) for k, v in dimensions.items()}

        # Overall score (weighted)
        weights = {
            "technical_expertise": 0.25,
            "architecture": 0.15,
            "open_source": 0.15,
            "community": 0.10,
            "leadership": 0.10,
            "documentation": 0.05,
            "testing": 0.05,
            "devops": 0.05,
            "ai": 0.05,
            "security": 0.05,
        }

        overall = sum(normalized[k] * weights[k] for k in weights)

        return {
            "overall": round(overall, 1),
            "dimensions": normalized,
        }

    @staticmethod
    def _technical_expertise(stats: ContributionStats) -> float:
        # Based on language diversity, commit frequency, code volume
        lang_diversity = len(stats.languages) / 10  # Max 10 languages
        commit_consistency = len(stats.commit_frequency) / 12  # Monthly for a year
        return min(1.0, (lang_diversity + commit_consistency) / 2)

    @staticmethod
    def _architecture(stats: ContributionStats) -> float:
        # Look for architecture patterns in repos
        arch_keywords = ["microservice", "event sourcing", "cqrs", "ddd", "clean architecture"]
        score = sum(
            1
            for kw in arch_keywords
            if any(kw in str(v).lower() for v in stats.frameworks.values())
        )
        return min(1.0, score / len(arch_keywords))

    @staticmethod
    def _open_source(profile: GitHubProfile, stats: ContributionStats) -> float:
        public_repos_ratio = profile.public_repos / max(1, profile.public_repos + profile.followers)
        stars_per_repo = sum(v for v in stats.languages.values()) / max(1, profile.public_repos)
        return min(1.0, (public_repos_ratio + min(1.0, stars_per_repo / 100)) / 2)

    @staticmethod
    def _community(profile: GitHubProfile, stats: ContributionStats) -> float:
        # PRs, issues, reviews
        total_interactions = stats.total_prs + stats.total_issues + stats.total_reviews
        return min(1.0, total_interactions / 100)

    @staticmethod
    def _leadership(stats: ContributionStats) -> float:
        # Maintainer roles, org memberships
        return min(1.0, stats.total_reviews / 50)

    @staticmethod
    def _documentation(stats: ContributionStats) -> float:
        # README quality, docs in repos
        return 0.5  # Placeholder

    @staticmethod
    def _testing(stats: ContributionStats) -> float:
        # Test frameworks, coverage tools
        test_frameworks = ["pytest", "jest", "junit", "testing-library", "cypress"]
        score = sum(1 for fw in test_frameworks if fw in stats.frameworks)
        return min(1.0, score / len(test_frameworks))

    @staticmethod
    def _devops(stats: ContributionStats) -> float:
        # CI/CD, Docker, Kubernetes, Terraform
        devops_tools = [
            "docker",
            "kubernetes",
            "github actions",
            "gitlab ci",
            "terraform",
            "ansible",
            "helm",
        ]
        score = sum(1 for tool in devops_tools if tool in str(stats.frameworks).lower())
        return min(1.0, score / len(devops_tools))

    @staticmethod
    def _ai(stats: ContributionStats) -> float:
        # ML/AI frameworks
        ai_tools = [
            "tensorflow",
            "pytorch",
            "sklearn",
            "langchain",
            "openai",
            "transformers",
            "huggingface",
        ]
        score = sum(1 for tool in ai_tools if tool in str(stats.frameworks).lower())
        return min(1.0, score / len(ai_tools))

    @staticmethod
    def _security(stats: ContributionStats) -> float:
        # Security tools, practices
        sec_tools = ["bandit", "safety", "snyk", "dependabot", "codeql", "semgrep"]
        score = sum(1 for tool in sec_tools if tool in str(stats.frameworks).lower())
        return min(1.0, score / len(sec_tools))
