# Configure NuGet Package Publishing

This guide will help you complete the setup for automated NuGet package publishing.

## Step 1: Create Personal Access Token

1. Go to GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Click "Generate new token (classic)"
3. Set the following:
   - **Expiration**: No expiration (or set a long duration)
   - **Scopes**: Select these permissions:
     - ✅ `repo` (Full control of private repositories)
     - ✅ `write:packages` (Upload packages to GitHub Package Registry)
     - ✅ `read:packages` (Download packages from GitHub Package Registry)

4. Copy the generated token (it will look like `ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`)

## Step 2: Configure Repository Secrets

Run these commands to add the secrets to the repository:

```bash
# Add GitHub Packages token
gh secret set PACKAGES_PAT --body "YOUR_TOKEN_HERE"

# Optional: Add NuGet.org API key (if you want to publish to NuGet.org)
gh secret set NUGET_API_KEY --body "YOUR_NUGET_API_KEY"
```

## Step 3: Test the Publishing

After configuring the secrets, trigger the publishing workflow:

```bash
# Trigger the workflow manually
gh workflow run "Publish NuGet Packages"

# Or make a small change to trigger it automatically
echo "# Trigger publishing" >> README.md
git add README.md
git commit -m "Trigger package publishing test"
git push
```

## Alternative: Manual Publishing

If you prefer to publish manually, you can use the built packages:

```bash
# Download the latest packages
gh run download --name "nuget-packages-*"

# Publish to GitHub Packages
dotnet nuget push *.nupkg \
  --source https://nuget.pkg.github.com/Third-Opinion/index.json \
  --api-key YOUR_PACKAGES_PAT \
  --skip-duplicate

# Publish to NuGet.org (optional)
dotnet nuget push *.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key YOUR_NUGET_API_KEY \
  --skip-duplicate
```

## Verification

Once configured, you can verify publishing works by:

1. Checking the Actions tab for successful runs
2. Viewing packages at: https://github.com/orgs/Third-Opinion/packages
3. Testing package consumption in other solutions

## Troubleshooting

- **403 Forbidden**: Token doesn't have `write:packages` permission
- **404 Not Found**: Incorrect source URL or repository name
- **401 Unauthorized**: Token is invalid or expired# Testing package publishing with PACKAGES_PAT
