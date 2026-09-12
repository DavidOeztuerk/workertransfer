{{/*
Gemeinsame Bausteine. Die interessanten sind die letzten beiden:
`workertransfer.serviceEnv` ist die eine Stelle, an der die Umgebung eines
Dienstes entsteht. Zur Python-Zeit holten Deployment UND Migrations-Job sie von
hier; seit der Dienst sein Schema beim Start selbst wandert, gibt es nur noch
einen Abnehmer — und damit keine Gelegenheit mehr, dass die beiden gegen
verschiedene Datenbanken laufen.
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

DB_PASSWORT steht bewusst VOR der Verbindungszeichenfolge: Kubernetes ersetzt
$(VAR) nur durch Variablen, die weiter oben in DERSELBEN Liste stehen. Aus
`envFrom` ginge es nicht — deshalb kommt das Passwort hier einzeln.
*/}}
{{- define "workertransfer.serviceEnv" -}}
{{- $root := .root -}}
{{- $svc := .svc -}}
- name: SERVICE_DIR
  value: {{ $svc.dir | quote }}
{{/*
0.0.0.0, nicht localhost: im Behälter beantwortete der Dienstport sonst nichts.
*/}}
- name: ASPNETCORE_URLS
  value: {{ printf "http://0.0.0.0:%v" $svc.port | quote }}
- name: DB_PASSWORT
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: POSTGRES_PASSWORD
{{/*
Der Schlüssel heisst wie die Datenbank, weil der Dienst sie so erfragt:
`GetConnectionString("<name>")`. Zwei Namen für dieselbe Sache wären zwei
Gelegenheiten, sie auseinanderlaufen zu lassen.
*/}}
- name: {{ printf "ConnectionStrings__%s" $svc.database }}
  value: {{ printf "Host=%s;Port=%v;Database=%s;Username=%s;Password=$(DB_PASSWORT)" $root.Values.postgres.host $root.Values.postgres.port $svc.database $root.Values.postgres.user | quote }}
{{/*
Die drei geteilten Geheimnisse. Einzeln aufgeführt statt als `envFrom` über das
ganze Secret: sonst bekäme JEDER Dienst auch den Schlüssel der
Formulierungshilfe, und einer, den neun von zwölf nicht brauchen, gehört nicht
in ihre Umgebung.
*/}}
- name: JwtSettings__Secret
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_JWT_SECRET
- name: Erasure__Geheimnis
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_ERASURE_SECRET
{{/*
Das Meldegeheimnis heisst je nach Seite anders und ist DASSELBE: bei
identity-service ist es `Notify__Geheimnis` (er prüft es), bei jedem Dienst
mit `Identity__Adresse` `Identity__Geheimnis` (er legt es vor), und die
absendenden Dienste tragen es als `Notifications__Geheimnis`.

Ausdrücklich ein ANDERES als das der Löschung (ADR-0027 §4.4): "darf eine Mail
anstossen" und "darf alles über einen Menschen löschen" dürfen nicht dasselbe
Papier sein. Die Bedingungen sind unabhängig: applications-service hat BEIDE
Adressen und braucht BEIDE Namen für dasselbe Geheimnis.
*/}}
{{- if or (eq $svc.name "identity-service") (eq $svc.name "profile-service") }}
{{/*
profile-service prueft es an seiner internen Suchtuer (ADR-0036). LEER hiesse
dort: die Tuer ist zu — sie antwortet 404, und der Scout suchte lautlos ins
Leere.
*/}}
- name: Notify__Geheimnis
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if hasKey ($svc.env | default dict) "Identity__Adresse" }}
- name: Identity__Geheimnis
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if hasKey ($svc.env | default dict) "Profile__Adresse" }}
{{/*
Dieselbe Kennung noch einmal unter einem vierten Namen: scout-service sucht
durch die interne Tuer von profile-service. LEER hiesse dort nicht „alles",
sondern „es wird nicht gesucht" — und zwar als Fehlschlag, damit „nicht
eingerichtet" nicht wie „es gibt niemanden" aussieht.
*/}}
- name: Profile__Geheimnis
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if hasKey ($svc.env | default dict) "Notifications__Adresse" }}
- name: Notifications__Geheimnis
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_NOTIFY_SECRET
{{- end }}
{{- if $svc.drafting }}
{{/*
Die Formulierungshilfe (ADR-0024). LEER heisst: sie ist aus, und die
Oberfläche sagt das — es wird dann kein fremder Dienst angerufen.
*/}}
- name: Draft__Schluessel
  valueFrom:
    secretKeyRef:
      name: {{ include "workertransfer.secretName" $root }}
      key: WORKER_ANTHROPIC_API_KEY
- name: Draft__Modell
  value: {{ $root.Values.draftingModel | quote }}
- name: Draft__Adresse
  value: {{ $root.Values.draftingUrl | quote }}
{{- end }}
{{- range $key, $value := ($svc.env | default dict) }}
- name: {{ $key }}
  value: {{ $value | quote }}
{{- end }}
{{- end -}}
