{{- define "pspsps.fullname" -}}
{{- if contains .Chart.Name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "pspsps.labels" -}}
app.kubernetes.io/name: {{ .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- /* Selector labels of one component: server, postgres or redis. */ -}}
{{- define "pspsps.selectorLabels" -}}
app.kubernetes.io/name: {{ .root.Chart.Name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{- define "pspsps.postgresHost" -}}
{{ include "pspsps.fullname" . }}-postgres
{{- end -}}

{{- define "pspsps.redisHost" -}}
{{ include "pspsps.fullname" . }}-redis
{{- end -}}

{{- /* The pod securityContext of PostgreSQL and of the backup job, which reads and writes as the same user. */ -}}
{{- define "pspsps.postgresPodSecurityContext" -}}
runAsNonRoot: true
# The alpine image's postgres user.
runAsUser: 70
runAsGroup: 70
fsGroup: 70
# Only when the volume's root has the wrong group: not a walk over every file on every start.
fsGroupChangePolicy: OnRootMismatch
seccompProfile:
  type: RuntimeDefault
{{- end -}}

{{- define "pspsps.imageRef" -}}
{{ .repository }}:{{ .tag }}
{{- end -}}

{{- /*
The collector's OTLP/HTTP base for the browsers' telemetry: telemetry.browserOtlpEndpoint, else telemetry.endpoint as it
is (http/protobuf) or with its gRPC port 4317 made 4318.
*/ -}}
{{- define "pspsps.browserOtlpEndpoint" -}}
{{- $t := .Values.telemetry -}}
{{- if $t.browserOtlpEndpoint -}}
{{ $t.browserOtlpEndpoint }}
{{- else if eq $t.protocol "http/protobuf" -}}
{{ $t.endpoint }}
{{- else if regexMatch ":4317/?$" $t.endpoint -}}
{{ regexReplaceAll ":4317(/?)$" $t.endpoint ":4318${1}" }}
{{- else -}}
{{ fail "telemetry.browser with a gRPC endpoint not on port 4317: set telemetry.browserOtlpEndpoint to the collector's OTLP/HTTP address" }}
{{- end -}}
{{- end -}}
