using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DurableTask.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace ServerlessImageProcessor
{
    public static class DurableSkeletonTest
    {
        [Function(nameof(TestOrchestrator))]
        public static async Task<string> TestOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var retryOptions = TaskOptions.FromRetryPolicy(new RetryPolicy(
                maxNumberOfAttempts: 3,
                firstRetryInterval: TimeSpan.FromSeconds(2),
                backoffCoefficient: 2.0));
                
            string result = await context.CallActivityAsync<string>(nameof(TestActivity), "world");
            return result;
        }
        
        [Function(nameof(TestActivity))]
        public static string TestActivity([ActivityTrigger] string name)
        {
            return $"Hello, {name}!";
        }

        [Function("TestStarter")]
        public static async Task<string> TestStarter(
            [BlobTrigger("test-trigger/{name}")] byte[] _,
            [DurableClient] DurableTaskClient client,
            string name)
        {
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(TestOrchestrator));
            return instanceId;
        }
        

    }
}