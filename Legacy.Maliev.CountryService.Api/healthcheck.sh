#!/bin/sh
set -eu

status=$(curl --disable --noproxy '*' --silent --show-error --max-time 2 \
    --output /dev/null --write-out '%{http_code}' \
    "http://127.0.0.1:${1:?port required}/countries/liveness")
[ "$status" = 200 ]
