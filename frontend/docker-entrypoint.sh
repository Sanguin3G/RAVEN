#!/bin/sh
set -eu

if [ -z "${RAVEN_API_ORIGIN:-}" ]; then
  echo "RAVEN_API_ORIGIN must be set to the API service origin." >&2
  exit 1
fi

case "$RAVEN_API_ORIGIN" in
  https://*|http://*) ;;
  *) echo "RAVEN_API_ORIGIN must be an absolute HTTP(S) origin." >&2; exit 1 ;;
esac

case "$RAVEN_API_ORIGIN" in
  *[!A-Za-z0-9.:/-]*) echo "RAVEN_API_ORIGIN contains unsupported characters." >&2; exit 1 ;;
esac

api_authority="${RAVEN_API_ORIGIN#*://}"
case "$api_authority" in
  ""|*/*) echo "RAVEN_API_ORIGIN must not include a path." >&2; exit 1 ;;
esac

envsubst '${RAVEN_API_ORIGIN}' \
  < /etc/nginx/templates/default.conf.template \
  > /etc/nginx/conf.d/default.conf

exec nginx -g 'daemon off;'
