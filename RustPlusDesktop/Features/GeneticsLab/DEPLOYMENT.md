# Genetics Lab - Coolify Deployment Guide

This guide walks you through deploying **Genetics Lab** to your self-hosted **Coolify** instance under **`https://genetics.rustplusdesktop.cloud`**.

---

## Architecture Overview

- **Type**: Single Page Application (React 18 + Vite + TypeScript + MUI).
- **Server**: Ultra-lightweight multi-stage `nginx:alpine` container.
- **Port**: `80` (HTTP internal, terminated as HTTPS via Coolify's Traefik/Caddy proxy).
- **WASM Support**: Full local offline Tesseract.js OCR engine and Web Worker multi-threading.
- **Healthcheck**: Endpoint available at `/healthz`.

---

## Prerequisites

1. **DNS Record**:
   - Create an **A Record** or **CNAME Record** in your DNS provider (Cloudflare, Namecheap, etc.):
     - **Name / Host**: `genetics` (or `genetics.rustplusdesktop.cloud`)
     - **Target / Value**: Your Coolify server IP address (or CNAME target).
     - **TTL**: Auto / 5 minutes.
     - *(If using Cloudflare, you can set Proxy Status to "DNS Only" or "Proxied" with SSL set to "Full / Strict")*.

2. **Coolify Instance**:
   - Access to your Coolify dashboard (e.g., `https://coolify.yourdomain.com`).

---

## Deployment Steps in Coolify

### Option A: Deploy from GitHub Repository (Recommended)

1. Open your **Coolify Dashboard** and navigate to your **Project / Environment**.
2. Click **+ New Application** -> **Public Repository** (or **Private Repository** if linked with GitHub App).
3. Enter the repository URL:
   - If using dedicated repo: `https://github.com/JawadYzbk/rust-genetics-lab`
   - If using monorepo: `https://github.com/JawadYzbk/rustplus-desktop`
4. Configure application settings:
   - **Build Pack**: `Dockerfile`
   - **Base Directory**: `/` (if dedicated repo) OR `/RustPlusDesktop/Features/GeneticsLab` (if monorepo)
   - **Port**: `80`
   - **Domains**: `https://genetics.rustplusdesktop.cloud`
5. *(Optional)* Under **Healthcheck**, set the path to `/healthz`.
6. Click **Deploy**.

---

### Option B: Deploy via Docker Compose

1. In Coolify, click **+ New Application** -> **Docker Compose**.
2. Select your repository or paste the contents of `docker-compose.yml`.
3. Set the domain to `https://genetics.rustplusdesktop.cloud`.
4. Click **Deploy**.

---

## Environment Variables (Optional)

Genetics Lab is client-side and requires **zero required environment variables** to run.

---

## Verification & Post-Deployment Checklist

Once Coolify completes the deployment:

1. **Visit Domain**:
   Open `https://genetics.rustplusdesktop.cloud` in your browser.
2. **Verify HTTPS**:
   Check that Coolify provisioned the Let's Encrypt SSL certificate automatically.
3. **Verify OCR Scanner**:
   - Click **Scanner** in the header.
   - Test by uploading or pasting a screenshot containing plant genes (e.g., `GGYYWW`).
   - Confirm local Tesseract WASM initializes and recognizes genes.
4. **Verify Calculations**:
   - Add clones to the **Clone Bank** and generate breeding combinations.
   - Verify that background Web Workers run the simulation smoothly.
5. **Verify Health Endpoint**:
   - Navigate to `https://genetics.rustplusdesktop.cloud/healthz` — should return `OK`.
