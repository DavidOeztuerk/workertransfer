{{/*
Gemeinsame Bausteine. Die interessanten sind die letzten beiden:
`workertransfer.serviceEnv` ist die eine Stelle, an der die Umgebung eines
Dienstes entsteht — Deployment und Migrations-Job holen sie beide von hier,
damit eine Migration nie gegen eine andere Datenbank läuft als der Dienst.
*/}}

{{- define "workertransfer.labels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end -}}

{{/* Der Name des Secrets — entweder ein fremdverwaltetes oder unser eigenes. */}}
{{- define "workertransfer.secretName" -}}
{{- if .Values.secrets.existingSecret -}}
{{- .Values.secrets.existingSecret -}}
{{- else -}}
{{- printf "%s-secrets" .Release.Name -}}
{{- end -}}
{{- end -}}

{{/*
Ein Geheimnis: der gewünschte Wert, sonst der bereits im Cluster liegende,
sonst ein frisch gewürfelter.

Die mittlere Stufe ist die wichtige. Ohne sie erzeugte jedes `helm upgrade` ein
neues WORKER_JWT_SECRET und meldete damit jede offene Sitzung ab — ein Upgrade,
das Menschen auswirft, ist keines.

Aufruf: {{ include "workertransfer.keepOrMake" (dict "data" $old "key" "X" "wanted" .Values...) }}
*/}}
{{- define "workertransfer.keepOrMake" -}}
{{- if .wanted -}}
{{- .wanted -}}
{{- else if hasKey .data .key -}}
{{- index .data .key | b64dec -}}
{{- else -}}
{{- randAlphaNum 48 -}}
{{- end -}}
{{- end -}}

{{/* Der Ursprung, unter dem der Browser die Anwendung erreicht. */}}
{{- define "workertransfer.apiOrigin" -}}
{{- .Values.web.apiOrigin | default .Values.publicUrl -}}
{{- end -}}

{{/*
Die Umgebung eines Dienstes.

Aufruf: {{ include "workertransfer.serviceEnv" (dict "root" $ "svc" $svc) }}

WORKER_DB_PASSWORD steht bewusst VOR WORKER_DATABASE_URL: Kubernetes ersetzt
$(VAR) nur durch Variablen, die weiter oben in derselben Liste stehen. Aus
`envFrom` ginge es nicht — deshalb kommt das Passwort hier einzeln.
*/}}
{{- define "workertransfer.serviceEnv" -}}
{{- $root := .root -}}
{{- $svc := .svc -}}
- name: SERVICE_DIR
  value: {{ $svc.dir | quote }}
- name: WORKER_PORT
  value: {{ $svc.port | quote }}
- name: WORKER_DB_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: POSTGRES_PASSWORD
- name: WORKER_DATABASE_URL
  value: {{ printf "postgresql+asyncpg://%s:$(WORKER_DB_PASSWORD)@%s:%v/%s" $root.Values.postgres.user $root.Values.postgres.host $root.Values.postgres.port $svc.database | quote }}
{{/*
Die drei geteilten Geheimnisse. Einzeln aufgeführt statt als `envFrom` über das
ganze Secret: sonst bekäme JEDER Dienst auch WORKER_ANTHROPIC_API_KEY, und ein
Schlüssel, den acht von zehn Diensten nicht brauchen, gehört nicht in ihre
Umgebung.
*/}}
- name: WORKER_JWT_SECRET
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_JWT_SECRET
- name: WORKER_NOTIFY_SECRET
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
- name: WORKER_ERASURE_SECRET
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_ERASURE_SECRET
{{- if $svc.drafting }}
{{/*
Die Formulierungshilfe (ADR-0024). LEER heisst: sie ist aus, und die
Oberfläche sagt das — es wird dann kein fremder Dienst angerufen.
*/}}
- name: WORKER_ANTHROPIC_API_KEY
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_ANTHROPIC_API_KEY
- name: WORKER_DRAFTING_MODEL
  value: {{ $root.Values.draftingModel | quote }}
{{- end }}
{{- range $key, $value := ($svc.env | default dict) }}
- name: {{ $key }}
  value: {{ $value | quote }}
{{- end }}
{{- end -}}
