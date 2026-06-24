#!/bin/bash
set -e
docker compose -f docker-compose.cloud.yml up --pull always
