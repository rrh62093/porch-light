#!/usr/bin/env bash
# Builds the PWA and uploads it to S3, then invalidates CloudFront.
# Usage: scripts/deploy-web.sh <bucket-name> <cloudfront-distribution-id>
set -euo pipefail
BUCKET=$1; DIST=$2
cd "$(dirname "$0")/../web"
npm ci
npm run build
# Hashed assets can be cached forever; everything else must revalidate (index.html, sw.js, manifest).
aws s3 sync dist/assets "s3://$BUCKET/assets" --cache-control "public,max-age=31536000,immutable" --delete
aws s3 sync dist "s3://$BUCKET" --exclude "assets/*" --cache-control "no-cache" --delete
aws cloudfront create-invalidation --distribution-id "$DIST" --paths "/*" >/dev/null
echo "Deployed."
