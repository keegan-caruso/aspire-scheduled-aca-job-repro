# Aspire scheduled Container App Job deploy repro

This is a minimal standalone repro for an Aspire CLI 13.3.x deploy bug: `aspire publish` honors `PublishAsScheduledAzureContainerAppJob` on a `ProjectResource` and emits a `Microsoft.App/jobs` Bicep resource, but the deploy pipeline routes the same resource through the plain Container App path (`provision-worker-containerapp`). With a real Azure deploy, that path provisions `Microsoft.App/containerApps/worker` instead of `Microsoft.App/jobs/worker`.

## Version pin

The AppHost pins `Aspire.AppHost.Sdk`, `Aspire.Hosting.AppHost`, and `Aspire.Hosting.Azure.AppContainers` to `13.3.5` in `Repro.AppHost/Repro.AppHost.csproj`. The local tool manifest pins `aspire.cli` to `13.3.5` in `.config/dotnet-tools.json`; run `dotnet tool restore` before invoking `dotnet tool run aspire -- ...`.

## Repro steps

Assumes the .NET 10 SDK is installed.

1. Restore the pinned Aspire CLI and build:

   ```sh
   cd repro
   dotnet tool restore
   dotnet build -c Release
   ```

2. Generate publish artifacts and confirm publish is correct:

   ```sh
   dotnet tool run aspire -- publish --apphost Repro.AppHost/Repro.AppHost.csproj --output-path aspire-output --non-interactive --nologo
   rg -n "Microsoft\.App/jobs|triggerType|cronExpression" aspire-output/worker/worker.bicep
   ```

   Expected output includes:

   ```text
   resource worker 'Microsoft.App/jobs@2025-07-01' = {
   triggerType: 'Schedule'
   cronExpression: '*/5 * * * *'
   ```

3. List the deploy pipeline without touching Azure:

   ```sh
   dotnet tool run aspire -- deploy --apphost Repro.AppHost/Repro.AppHost.csproj --output-path aspire-output --non-interactive --nologo --list-steps | rg "provision-worker"
   ```

   Expected: deploy should route `worker` through the Container App Job path and ultimately deploy `Microsoft.App/jobs/worker`.

   Actual on `aspire.cli` `13.3.5`:

   ```text
   14. provision-worker-containerapp
   ```

   The `containerapp` step name is the deploy-side divergence from the publish artifact above. A real `aspire deploy` continues down that path and creates a plain `Microsoft.App/containerApps/worker` resource.

## Local validation surface — no Azure required

`aspire deploy --list-steps` walks the deploy pipeline tree without invoking any provider, so the bug is visible **without an Azure subscription, without logging in, and without any artifacts on disk** other than the build output. The step name and its downstream dependants are the smoking gun:

```sh
dotnet tool run aspire -- deploy --apphost Repro.AppHost/Repro.AppHost.csproj --output-path aspire-output --non-interactive --nologo --list-steps
```

Tail of the actual output on `aspire.cli` `13.3.5`:

```text
13. push-worker
   ├─ Depends on: build-worker, push-prereq
   └─ Tags: push-container-image

14. provision-worker-containerapp
   ├─ Depends on: create-provisioning-context, provision-aca-env, push-worker
   └─ Tags: provision-infra

15. provision-azure-bicep-resources
   ├─ Depends on: deploy-prereq, create-provisioning-context, provision-aca-env-acr,
   │              provision-aca-env, provision-worker-containerapp
   └─ Tags: provision-infra

17. print-worker-summary
   ├─ Depends on: provision-worker-containerapp
   └─ Tags: print-summary
```

Step `14` is the divergence. `worker` has `PublishAsScheduledAzureContainerAppJob` applied (and `aspire publish` emits `Microsoft.App/jobs` Bicep for it), but the deploy pipeline names the provisioning step `provision-worker-**containerapp**` and the downstream `provision-azure-bicep-resources` / `print-worker-summary` steps both consume that wrong-typed step. There is no `provision-worker-containerappjob` (or equivalent) step anywhere in the tree.

Comparison: `aspire publish --list-steps` on the same AppHost shows a single opaque `publish-azure<hash>` step, with no per-resource provision steps — i.e. the publish side never makes the wrong choice because it doesn't make the choice at this layer at all; the deploy pipeline is the only place this routing happens.

## Secondary publish issue

`aspire publish` writes `aspire-output/worker/worker.bicep`, but `aspire-output/main.bicep` only references the ACA environment modules and does not include a `module worker ...` entry for the generated job module. That is separate from the deploy bug above, but worth noting in the upstream issue.
