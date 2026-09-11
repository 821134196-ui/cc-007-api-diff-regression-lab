FROM mcr.microsoft.com/playwright:v1.48.2-jammy
WORKDIR /tests
COPY tests/e2e/package.json ./
RUN npm install --no-audit --no-fund
COPY tests/e2e/ ./
CMD ["npx", "playwright", "test", "--reporter=list"]
