# Skill: Docker, Kubernetes, CI/CD, GitOps

## Purpose
Set up complete infrastructure for local development, CI/CD pipelines, and production deployment.

## Docker Compose (Local Development)

```yaml
# docker-compose.yml
version: "3.9"

services:
  # Databases
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: workertransfer
      POSTGRES_PASSWORD: workertransfer
      POSTGRES_MULTIPLE_DATABASES: identity,profile,jobs,applications,transfers,contracts,companies,messaging,notifications,ai,analytics
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./scripts/init-multi-db.sh:/docker-entrypoint-initdb.d/init-multi-db.sh
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U workertransfer"]
      interval: 5s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5

  # Message Broker
  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: workertransfer
      RABBITMQ_DEFAULT_PASS: workertransfer
    ports:
      - "5672:5672"
      - "15672:15672"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_running"]
      interval: 10s
      timeout: 5s
      retries: 5

  # Object Storage
  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: workertransfer
      MINIO_ROOT_PASSWORD: workertransfer
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - minio_data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 30s
      timeout: 20s
      retries: 3

  # Search
  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.15.0
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false
      - "ES_JAVA_OPTS=-Xms512m -Xmx512m"
    ports:
      - "9200:9200"
    volumes:
      - elasticsearch_data:/usr/share/elasticsearch/data
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:9200/_cluster/health || exit 1"]
      interval: 10s
      timeout: 10s
      retries: 10

  # Vector DB
  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"
      - "6334:6334"
    volumes:
      - qdrant_data:/qdrant/storage
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:6333/health"]
      interval: 10s
      timeout: 5s
      retries: 5

  # Observability
  jaeger:
    image: jaegertracing/all-in-one:1.53
    ports:
      - "16686:16686"
      - "6831:6831/udp"
      - "14268:14268"
    environment:
      COLLECTOR_OTLP_ENABLED: "true"

  prometheus:
    image: prom/prometheus:v2.53
    ports:
      - "9090:9090"
    volumes:
      - ./monitoring/prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus
    command:
      - "--config.file=/etc/prometheus/prometheus.yml"
      - "--storage.tsdb.path=/prometheus"

  grafana:
    image: grafana/grafana:10.4
    ports:
      - "3000:3000"
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - ./monitoring/grafana/dashboards:/etc/grafana/provisioning/dashboards
      - ./monitoring/grafana/datasources:/etc/grafana/provisioning/datasources
      - grafana_data:/var/lib/grafana
    depends_on:
      - prometheus

  # API Gateway
  traefik:
    image: traefik:v3.0
    command:
      - "--api.insecure=true"
      - "--providers.docker=true"
      - "--providers.docker.exposedbydefault=false"
      - "--entrypoints.web.address=:80"
      - "--entrypoints.websecure.address=:443"
      - "--certificatesresolvers.le.acme.email=admin@workertransfer.com"
      - "--certificatesresolvers.le.acme.storage=/letsencrypt/acme.json"
      - "--certificatesresolvers.le.acme.tlschallenge=true"
      - "--log.level=DEBUG"
    ports:
      - "80:80"
      - "443:443"
      - "8080:8080"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./letsencrypt:/letsencrypt
    labels:
      - "traefik.enable=true"

volumes:
  postgres_data:
  redis_data:
  rabbitmq_data:
  minio_data:
  elasticsearch_data:
  qdrant_data:
  prometheus_data:
  grafana_data:
  letsencrypt:
```

## Service Dockerfile Template

```dockerfile
# Dockerfile (multi-stage)
# Stage 1: Builder
FROM python:3.14-slim AS builder

WORKDIR /app

# Install uv
COPY --from=ghcr.io/astral-sh/uv:latest /uv /usr/local/bin/uv

# Enable bytecode compilation
ENV UV_COMPILE_BYTECODE=1

# Copy workspace files
COPY pyproject.toml uv.lock ./
COPY packages/ ./packages/
COPY apps/ ./apps/

# Install dependencies
RUN uv sync --all-packages --all-groups --no-dev

# Stage 2: Runtime
FROM python:3.14-slim AS runtime

WORKDIR /app

# Create non-root user
RUN groupadd -r workertransfer && useradd -r -g workertransfer workertransfer

# Copy from builder
COPY --from=builder /app /app

# Set ownership
RUN chown -R workertransfer:workertransfer /app

USER workertransfer

# Expose port
EXPOSE 8000

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8000/health/live || exit 1

# Run service
CMD ["uv", "run", "worker-identity"]
```

## Kubernetes Manifests (Helm Chart Structure)

```
deployment/
├── charts/
│   ├── workertransfer-platform/        # Platform umbrella chart
│   │   ├── Chart.yaml
│   │   ├── values.yaml
│   │   └── templates/
│   │       ├── namespace.yaml
│   │       ├── network-policies.yaml
│   │       └── _helpers.tpl
│   ├── identity-service/
│   │   ├── Chart.yaml
│   │   ├── values.yaml
│   │   ├── values-prod.yaml
│   │   └── templates/
│   │       ├── deployment.yaml
│   │       ├── service.yaml
│   │       ├── ingress.yaml
│   │       ├── hpa.yaml
│   │       ├── servicemonitor.yaml
│   │       ├── configmap.yaml
│   │       └── secret.yaml
│   ├── profile-service/
│   ├── jobs-service/
│   ├── applications-service/
│   ├── transfers-service/
│   ├── contracts-service/
│   ├── companies-service/
│   ├── ai-service/
│   ├── messaging-service/
│   ├── notifications-service/
│   ├── search-service/
│   ├── analytics-service/
│   ├── admin-service/
│   ├── gateway/
│   └── frontend/
├── overlays/
│   ├── dev/
│   ├── staging/
│   └── prod/
└── argocd/
    ├── applications/
    │   ├── platform.yaml
    │   ├── identity.yaml
    │   ├── profile.yaml
    │   └── ...
    └── projects/
        └── workertransfer.yaml
```

## GitHub Actions CI/CD

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]

env:
  PYTHON_VERSION: "3.14"
  NODE_VERSION: "24"
  PNPM_VERSION: "11"

jobs:
  # Python linting & type checking
  python-lint:
    name: Python Lint & Type Check
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: ${{ env.PYTHON_VERSION }}
      
      - name: Install uv
        uses: astral-sh/setup-uv@v3
        with:
          enable-cache: true
      
      - name: Sync dependencies
        run: uv sync --all-packages --all-groups
      
      - name: Check formatting
        run: uv run ruff format --check .
      
      - name: Lint
        run: uv run ruff check .
      
      - name: Type check
        run: uv run mypy packages apps

  # Python tests
  python-test:
    name: Python Tests
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_USER: test
          POSTGRES_PASSWORD: test
          POSTGRES_DB: test
        ports: 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
      redis:
        image: redis:7-alpine
        ports: 6379:6379
        options: --health-cmd "redis-cli ping" --health-interval 10s --health-timeout 5s --health-retries 5
      rabbitmq:
        image: rabbitmq:3.13-alpine
        ports: 5672:5672
        options: --health-cmd "rabbitmq-diagnostics check_running" --health-interval 10s --health-timeout 5s --health-retries 5
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: ${{ env.PYTHON_VERSION }}
      
      - name: Install uv
        uses: astral-sh/setup-uv@v3
        with:
          enable-cache: true
      
      - name: Sync dependencies
        run: uv sync --all-packages --all-groups
      
      - name: Run tests
        run: uv run pytest --cov=worker_core --cov=worker_platform --cov-report=xml
        env:
          DATABASE_URL: postgresql://test:test@localhost:5432/test
          REDIS_URL: redis://localhost:6379/0
          RABBITMQ_URL: amqp://guest:guest@localhost:5672/
      
      - name: Upload coverage
        uses: codecov/codecov-action@v4
        with:
          files: ./coverage.xml

  # Frontend linting & type checking
  frontend-lint:
    name: Frontend Lint & Type Check
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Node
        uses: actions/setup-node@v4
        with:
          node-version: ${{ env.NODE_VERSION }}
          cache: 'pnpm'
      
      - name: Install pnpm
        run: corepack enable && corepack prepare pnpm@${{ env.PNPM_VERSION }} --activate
      
      - name: Install dependencies
        run: pnpm install --frozen-lockfile
      
      - name: Type check
        run: pnpm check
      
      - name: Lint
        run: pnpm lint
      
      - name: Format check
        run: pnpm format:check

  # Frontend tests
  frontend-test:
    name: Frontend Tests
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Node
        uses: actions/setup-node@v4
        with:
          node-version: ${{ env.NODE_VERSION }}
          cache: 'pnpm'
      
      - name: Install pnpm
        run: corepack enable && corepack prepare pnpm@${{ env.PNPM_VERSION }} --activate
      
      - name: Install dependencies
        run: pnpm install --frozen-lockfile
      
      - name: Run tests
        run: pnpm test --coverage
      
      - name: Upload coverage
        uses: codecov/codecov-action@v4
        with:
          files: ./coverage/lcov.info

  # E2E tests
  e2e-test:
    name: E2E Tests
    runs-on: ubuntu-latest
    needs: [python-test, frontend-test]
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Docker Compose
        run: docker compose up -d
      
      - name: Wait for services
        run: sleep 30
      
      - name: Run Playwright tests
        run: pnpm test:e2e
        working-directory: ./apps/web

  # Build & Push Docker images
  build:
    name: Build & Push Images
    runs-on: ubuntu-latest
    needs: [python-lint, python-test, frontend-lint, frontend-test]
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3
      
      - name: Login to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Build and push services
        run: |
          for service in identity-service profile-service jobs-service applications-service transfers-service contracts-service companies-service ai-service messaging-service notifications-service search-service analytics-service admin-service gateway frontend; do
            docker buildx build \
              --platform linux/amd64,linux/arm64 \
              --tag ghcr.io/${{ github.repository }}/$service:${{ github.sha }} \
              --tag ghcr.io/${{ github.repository }}/$service:latest \
              --push \
              ./apps/$service
          done

  # Deploy to staging
  deploy-staging:
    name: Deploy to Staging
    runs-on: ubuntu-latest
    needs: build
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    environment: staging
    steps:
      - uses: actions/checkout@v4
      
      - name: Deploy to staging
        run: |
          kubectl config use-context staging
          kubectl apply -k deployment/overlays/staging
          kubectl rollout status deployment --all -n workertransfer-staging

  # Deploy to production
  deploy-prod:
    name: Deploy to Production
    runs-on: ubuntu-latest
    needs: deploy-staging
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    environment: production
    steps:
      - uses: actions/checkout@v4
      
      - name: Deploy to production
        run: |
          kubectl config use-context production
          kubectl apply -k deployment/overlays/prod
          kubectl rollout status deployment --all -n workertransfer-prod
```

## ArgoCD Application

```yaml
# deployment/argocd/applications/platform.yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: workertransfer-platform
  namespace: argocd
  finalizers:
    - resources-finalizer.argocd.argoproj.io
spec:
  project: workertransfer
  source:
    repoURL: https://github.com/your-org/workertransfer.git
    targetRevision: main
    path: deployment/charts/workertransfer-platform
    helm:
      valueFiles:
        - values.yaml
        - values-staging.yaml
  destination:
    server: https://kubernetes.default.svc
    namespace: workertransfer
  syncPolicy:
    automated:
      prune: true
      selfHeal: true
      allowEmpty: false
    syncOptions:
      - CreateNamespace=true
      - PrunePropagationPolicy=foreground
      - PruneLast=true
    retry:
      limit: 5
      backoff:
        duration: 5s
        factor: 2
        maxDuration: 3m
```

## Monitoring Stack

```yaml
# monitoring/prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'prometheus'
    static_configs:
      - targets: ['localhost:9090']

  - job_name: 'kubernetes-apiservers'
    kubernetes_sd_configs:
      - role: endpoints
    scheme: https
    tls_config:
      ca_file: /var/run/secrets/kubernetes.io/serviceaccount/ca.crt
    bearer_token_file: /var/run/secrets/kubernetes.io/serviceaccount/token
    relabel_configs:
      - source_labels: [__meta_kubernetes_namespace, __meta_kubernetes_service_name, __meta_kubernetes_endpoint_port_name]
        action: keep
        regex: default;kubernetes;https

  - job_name: 'kubernetes-nodes'
    kubernetes_sd_configs:
      - role: node
    scheme: https
    tls_config:
      ca_file: /var/run/secrets/kubernetes.io/serviceaccount/ca.crt
    bearer_token_file: /var/run/secrets/kubernetes.io/serviceaccount/token
    relabel_configs:
      - action: labelmap
        regex: __meta_kubernetes_node_label_(.+)

  - job_name: 'workertransfer-services'
    kubernetes_sd_configs:
      - role: pod
        namespaces:
          names:
            - workertransfer
            - workertransfer-staging
            - workertransfer-prod
    relabel_configs:
      - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_scrape]
        action: keep
        regex: true
      - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_path]
        action: replace
        target_label: __metrics_path__
        regex: (.+)
      - source_labels: [__address__, __meta_kubernetes_pod_annotation_prometheus_io_port]
        action: replace
        regex: ([^:]+)(?::\d+)?;(\d+)
        replacement: $1:$2
        target_label: __address__
      - action: labelmap
        regex: __meta_kubernetes_pod_label_(.+)
      - source_labels: [__meta_kubernetes_namespace]
        action: replace
        target_label: kubernetes_namespace
      - source_labels: [__meta_kubernetes_pod_name]
        action: replace
        target_label: kubernetes_pod_name

  - job_name: 'rabbitmq'
    static_configs:
      - targets: ['rabbitmq:15692']

  - job_name: 'redis'
    static_configs:
      - targets: ['redis-exporter:9121']

  - job_name: 'postgres'
    static_configs:
      - targets: ['postgres-exporter:9187']
```

## Grafana Dashboard (Service Overview)

```json
{
  "dashboard": {
    "title": "WorkerTransfer Service Overview",
    "tags": ["workertransfer", "overview"],
    "timezone": "browser",
    "panels": [
      {
        "title": "Request Rate",
        "type": "graph",
        "datasource": "Prometheus",
        "targets": [
          {
            "expr": "sum(rate(http_requests_total[5m])) by (service, method, path)",
            "legendFormat": "{{service}} {{method}} {{path}}"
          }
        ]
      },
      {
        "title": "Error Rate",
        "type": "graph",
        "datasource": "Prometheus",
        "targets": [
          {
            "expr": "sum(rate(http_requests_total{status=~\"5..\"}[5m])) by (service)",
            "legendFormat": "{{service}} 5xx"
          },
          {
            "expr": "sum(rate(http_requests_total{status=~\"4..\"}[5m])) by (service)",
            "legendFormat": "{{service}} 4xx"
          }
        ]
      },
      {
        "title": "Latency (p50, p95, p99)",
        "type": "graph",
        "datasource": "Prometheus",
        "targets": [
          {
            "expr": "histogram_quantile(0.50, sum(rate(http_request_duration_seconds_bucket[5m])) by (service, le))",
            "legendFormat": "{{service}} p50"
          },
          {
            "expr": "histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (service, le))",
            "legendFormat": "{{service}} p95"
          },
          {
            "expr": "histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket[5m])) by (service, le))",
            "legendFormat": "{{service}} p99"
          }
        ]
      },
      {
        "title": "Active Connections",
        "type": "graph",
        "datasource": "Prometheus",
        "targets": [
          {
            "expr": "sum(active_connections) by (service)",
            "legendFormat": "{{service}}"
          }
        ]
      }
    ]
  }
}
```