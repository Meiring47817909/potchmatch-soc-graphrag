# Build stage - the full Node image (not alpine). Tailwind v4's PostCSS
# plugin (@tailwindcss/postcss) depends on lightningcss's native binary,
# which only ships glibc builds - alpine's musl libc can't load it, so the
# build stage needs a glibc-based image even though the final image doesn't.
FROM node:22 AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build -- --configuration production

# Runtime stage - just the compiled static files served by nginx. The
# Angular CLI's modern application builder (@angular/build:application)
# outputs the browser bundle under dist/<project-name>/browser - there's no
# SSR server target configured here, so that's the only output folder.
FROM nginx:1.27-alpine AS final
COPY --from=build /app/dist/squash-frontend/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
