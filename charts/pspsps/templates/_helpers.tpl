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
seccompProfile:
  type: RuntimeDefault
{{- end -}}

{{- define "pspsps.imageRef" -}}
{{ .repository }}:{{ .tag }}
{{- end -}}
