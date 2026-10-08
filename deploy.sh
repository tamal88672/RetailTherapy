#!/usr/bin/env bash
# Deploys to Google Cloud Run with Firestore. Easiest from Google Cloud Shell (gcloud is already logged in there).
#   PROJECT=my-gcp-project AMAZON_TAG=mytag-20 ./deploy.sh
set -euo pipefail

PROJECT="${PROJECT:?set PROJECT=your-gcp-project-id}"
AMAZON_TAG="${AMAZON_TAG:?set AMAZON_TAG=your-associates-tag}"
REGION="${REGION:-us-central1}"       # free-tier region
SERVICE="${SERVICE:-retail-therapy}"
DESIGN="${DESIGN:-v2-masonry}"        # v1-magazine | v2-masonry | v3-compact
ADSENSE_CLIENT="${ADSENSE_CLIENT:-ca-pub-XXXXXXXXXXXXXXXX}"

gcloud config set project "$PROJECT"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com \
  firestore.googleapis.com secretmanager.googleapis.com

# Firestore (native mode), created once
if [ -z "$(gcloud firestore databases list --format='value(name)' --quiet 2>/dev/null)" ]; then
  gcloud firestore databases create --location="$REGION" --type=firestore-native --quiet
fi

# Admin key for the product API, created once
if ! gcloud secrets describe retail-admin-key >/dev/null 2>&1; then
  openssl rand -hex 24 | gcloud secrets create retail-admin-key --data-file=-
  echo "Admin key created. Show it with: gcloud secrets versions access latest --secret=retail-admin-key"
fi

# Let the service read/write Firestore and read the secret
SA="$(gcloud projects describe "$PROJECT" --format='value(projectNumber)')-compute@developer.gserviceaccount.com"
gcloud projects add-iam-policy-binding "$PROJECT" --member="serviceAccount:$SA" \
  --role=roles/datastore.user --condition=None >/dev/null
gcloud secrets add-iam-policy-binding retail-admin-key --member="serviceAccount:$SA" \
  --role=roles/secretmanager.secretAccessor >/dev/null
# Newer projects build source deploys as the default compute account, which then needs build permissions
gcloud projects add-iam-policy-binding "$PROJECT" --member="serviceAccount:$SA" \
  --role=roles/cloudbuild.builds.builder --condition=None >/dev/null

gcloud run deploy "$SERVICE" --source . --region "$REGION" --allow-unauthenticated \
  --min-instances 0 --max-instances 2 --cpu 1 --memory 512Mi \
  --set-env-vars "Storage__Provider=Firestore,Storage__ProjectId=$PROJECT,Site__AmazonTag=$AMAZON_TAG,Site__LiveDesign=$DESIGN,Site__Ads__Client=$ADSENSE_CLIENT" \
  --set-secrets "Admin__ApiKey=retail-admin-key:latest"

echo "Live at: $(gcloud run services describe "$SERVICE" --region "$REGION" --format='value(status.url)')"
