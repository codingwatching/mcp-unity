using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using McpUnity.Tools;
using McpUnity.Unity;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for managing packages in the Unity Package Manager
    /// </summary>
    public class PackageManagerTool : McpToolBase
    {
        // Class to track each package operation
        private class PackageOperation
        {
            public AddRequest Request { get; set; }
            public TaskCompletionSource<JObject> CompletionSource { get; set; }
        }
        
        // Queue of active package operations
        private readonly List<PackageOperation> _activeOperations = new List<PackageOperation>();
        
        // Flag to track if the update callback is registered
        private bool _updateCallbackRegistered = false;
        
        public PackageManagerTool()
        {
            Name = "package_manager";
            Description = "Manages packages in the Unity Package Manager";
            IsAsync = true; // Package Manager operations are asynchronous
        }
        
        /// <summary>
        /// Execute the PackageManager tool asynchronously
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        /// <param name="tcs">TaskCompletionSource to set the result or exception</param>
        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                // Extract method parameter
                string method = parameters["methodSource"]?.ToObject<string>();
                if (string.IsNullOrEmpty(method))
                {
                    tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                        "Required parameter 'methodSource' not provided", 
                        "validation_error"
                    ));
                    return;
                }
                
                // Create and register the operation
                var operation = new PackageOperation
                {
                    CompletionSource = tcs
                };
                
                switch (method.ToLowerInvariant())
                {
                    case "registry":
                        operation.Request = AddFromRegistry(parameters, tcs);
                        break;
                    case "github":
                        operation.Request = AddFromGitHub(parameters, tcs);
                        break;
                    case "disk":
                        operation.Request = AddFromDisk(parameters, tcs);
                        break;
                    default:
                        tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                            $"Unknown method '{method}'. Valid methods are: registry, github, disk",
                            "validation_error"
                        ));
                        return;
                }
                
                // If request creation failed, the error has already been set on the tcs
                if (operation.Request == null)
                {
                    return;
                }
                
                lock (_activeOperations)
                {
                    _activeOperations.Add(operation);
                    
                    // Register update callback if not already registered
                    if (!_updateCallbackRegistered)
                    {
                        EditorApplication.update += CheckOperationsCompletion;
                        _updateCallbackRegistered = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any uncaught exceptions
                Debug.LogError($"[MCP Unity] Exception in PackageManagerTool.ExecuteAsync: {ex}");
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Internal error: {ex.Message}",
                    "internal_error"
                ));
            }
        }
        
        /// <summary>
        /// Add a package from the Unity registry
        /// </summary>
        private AddRequest AddFromRegistry(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Extract parameters
            string packageName = parameters["packageName"]?.ToObject<string>();
            if (string.IsNullOrEmpty(packageName))
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'packageName' not provided for registry method", 
                    "validation_error"
                ));
                return null;
            }
            
            string version = parameters["version"]?.ToObject<string>();
            string packageIdentifier = packageName;
            
            // Add version if specified
            if (!string.IsNullOrEmpty(version))
            {
                packageIdentifier = $"{packageName}@{version}";
            }
            
            Debug.Log($"[MCP Unity] Adding package from registry: {packageIdentifier}");
            
            try
            {
                // Add the package
                return Client.Add(packageIdentifier);
            }
            catch (Exception ex)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Exception adding package: {ex.Message}",
                    "package_manager_error"
                ));
                return null;
            }
        }
        
        /// <summary>
        /// Add a package from GitHub
        /// </summary>
        private AddRequest AddFromGitHub(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Extract parameters
            string packageUrl = parameters["repositoryUrl"]?.ToObject<string>();
            
            if (string.IsNullOrEmpty(packageUrl))
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'repositoryUrl' not provided for github method", 
                    "validation_error"
                ));
                return null;
            }
            
            string branch = parameters["branch"]?.ToObject<string>();
            string path = parameters["path"]?.ToObject<string>();
            
            // Remove any .git suffix if present
            if (packageUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                packageUrl = packageUrl.Substring(0, packageUrl.Length - 4);
            }
            
            // Add branch if specified
            if (!string.IsNullOrEmpty(branch))
            {
                packageUrl += "#" + branch;
            }
            
            // Add path if specified
            if (!string.IsNullOrEmpty(path))
            {
                if (!string.IsNullOrEmpty(branch))
                {
                    // Branch is already added, append path with slash
                    packageUrl += "/" + path;
                }
                else
                {
                    // No branch, use hash followed by path
                    packageUrl += "#" + path;
                }
            }
            
            Debug.Log($"[MCP Unity] Adding package from GitHub: {packageUrl}");
            
            try
            {
                // Add the package
                return Client.Add(packageUrl);
            }
            catch (Exception ex)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Exception adding package: {ex.Message}",
                    "package_manager_error"
                ));
                return null;
            }
        }
        
        /// <summary>
        /// Add a package from disk
        /// </summary>
        private AddRequest AddFromDisk(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Extract parameters
            string path = parameters["path"]?.ToObject<string>();
            
            if (string.IsNullOrEmpty(path))
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'path' not provided for disk method", 
                    "validation_error"
                ));
                return null;
            }
            
            // Format as file URL
            string packageUrl = $"file:{path}";
            
            Debug.Log($"[MCP Unity] Adding package from disk: {packageUrl}");
            
            try
            {
                // Add the package
                return Client.Add(packageUrl);
            }
            catch (Exception ex)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Exception adding package: {ex.Message}",
                    "package_manager_error"
                ));
                return null;
            }
        }
        
        /// <summary>
        /// Check all active operations for completion
        /// </summary>
        private void CheckOperationsCompletion()
        {
            // Store initial count
            int initialCount = _activeOperations.Count;
            
            lock (_activeOperations)
            {
                // Process operations in reverse order to safely remove completed ones
                for (int i = _activeOperations.Count - 1; i >= 0; i--)
                {
                    var operation = _activeOperations[i];
                    
                    if (operation.Request != null && operation.Request.IsCompleted)
                    {
                        // Process the completed operation
                        ProcessCompletedOperation(operation);
                        
                        // Remove it from the active operations list
                        _activeOperations.RemoveAt(i);
                    }
                }
                
                // If all operations are completed, unregister the update callback
                if (_activeOperations.Count == 0 && _updateCallbackRegistered)
                {
                    EditorApplication.update -= CheckOperationsCompletion;
                    _updateCallbackRegistered = false;
                }
            }
            
            // If any operations completed, force a GC collection to clean up UPM request objects
            if (initialCount != _activeOperations.Count)
            {
                GC.Collect();
            }
        }
        
        /// <summary>
        /// Process a completed package operation
        /// </summary>
        private void ProcessCompletedOperation(PackageOperation operation)
        {
            if (operation.CompletionSource == null)
            {
                Debug.LogError("[MCP Unity] TaskCompletionSource is null when processing completed operation");
                return;
            }
            
            // Check request status
            if (operation.Request.Status == StatusCode.Success)
            {
                var result = operation.Request.Result;
                if (result != null)
                {
                    operation.CompletionSource.SetResult(new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Successfully added package: {result.displayName} ({result.name}) version {result.version}",
                        ["type"] = "text",
                        ["packageInfo"] = JObject.FromObject(new
                        {
                            name = result.name,
                            displayName = result.displayName,
                            version = result.version
                        })
                    });
                }
                else
                {
                    operation.CompletionSource.SetResult(new JObject
                    {
                        ["success"] = true,
                        ["message"] = $"Package operation completed successfully, but no package information was returned."
                    });
                }
            }
            else if (operation.Request.Status == StatusCode.Failure)
            {
                operation.CompletionSource.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to add package: {operation.Request.Error.message}",
                    "package_manager_error"
                ));
            }
            else
            {
                operation.CompletionSource.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Unknown package manager status: {operation.Request.Status}",
                    "package_manager_error"
                ));
            }
        }
    }
}
