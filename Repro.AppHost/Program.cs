using Azure.Provisioning.AppContainers;

var builder = DistributedApplication.CreateBuilder(args);

var worker = builder.AddProject<Projects.Repro_Worker>("worker");

if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddAzureContainerAppEnvironment("aca-env");

    worker.PublishAsScheduledAzureContainerAppJob(
        "*/5 * * * *",
        (_, job) =>
        {
            job.Configuration.ReplicaTimeout = 60;
            job.Configuration.ReplicaRetryLimit = 0;
            job.Configuration.ScheduleTriggerConfig.Parallelism = 1;
            job.Configuration.ScheduleTriggerConfig.ReplicaCompletionCount = 1;
        }
    );
}

builder.Build().Run();
