# ==============================================================================
# Build Stage
# ==============================================================================
FROM node:22-alpine AS builder

WORKDIR /app

# Install dependencies with frozen lockfile for reproducible builds
COPY package.json package-lock.json ./
RUN npm ci

# Copy all source files and assets
COPY . .

# Build production distribution
RUN npm run build

# ==============================================================================
# Production Server Stage
# ==============================================================================
FROM nginx:1.27-alpine AS runner

# Remove default nginx static assets
RUN rm -rf /usr/share/nginx/html/*

# Copy built frontend assets from builder stage
COPY --from=builder /app/dist /usr/share/nginx/html

# Copy optimized nginx SPA configuration
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Coolify / HTTP Port
EXPOSE 80

# Health check to monitor container status in Coolify
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget --quiet --tries=1 --spider http://127.0.0.1:80/healthz || exit 1

# Start Nginx in foreground
CMD ["nginx", "-g", "daemon off;"]
