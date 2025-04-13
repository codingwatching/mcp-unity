import WebSocket from 'ws';
import { v4 as uuidv4 } from 'uuid';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { execSync } from 'child_process';
import { default as winreg } from 'winreg';
export class McpUnity {
    logger;
    port;
    ws = null;
    pendingRequests = new Map();
    REQUEST_TIMEOUT = 10000;
    constructor(logger) {
        this.logger = logger;
        // Initialize port from environment variable or use default
        const envRegistry = process.platform === 'win32'
            ? this.getUnityPortFromWindowsRegistry()
            : this.getUnityPortFromUnixRegistry();
        const envPort = process.env.UNITY_PORT || envRegistry;
        this.port = envPort ? parseInt(envPort, 10) : 8090;
        this.logger.info(`Using port: ${this.port} for Unity WebSocket connection`);
    }
    /**
     * Start the Unity connection
     * @param clientName Optional name of the MCP client connecting to Unity
     */
    async start(clientName) {
        try {
            this.logger.info('Attempting to connect to Unity WebSocket...');
            await this.connect(clientName); // Pass client name to connect
            this.logger.info('Successfully connected to Unity WebSocket');
            if (clientName) {
                this.logger.info(`Client identified to Unity as: ${clientName}`);
            }
        }
        catch (error) {
            this.logger.warn(`Could not connect to Unity WebSocket: ${error instanceof Error ? error.message : String(error)}`);
            this.logger.warn('Will retry connection on next request');
            // Disconnect to clean up for the next request attempt
            this.disconnect();
        }
        return Promise.resolve();
    }
    /**
     * Connect to the Unity WebSocket
     * @param clientName Optional name of the MCP client connecting to Unity
     */
    async connect(clientName) {
        if (this.isConnected) {
            this.logger.debug('Already connected to Unity WebSocket');
            return Promise.resolve();
        }
        // First, properly close any existing WebSocket connection
        this.disconnect();
        return new Promise((resolve, reject) => {
            const wsUrl = `ws://localhost:${this.port}/McpUnity`;
            this.logger.debug(`Connecting to ${wsUrl}...`);
            // Create connection options with headers for client identification
            const options = {
                headers: {
                    'X-Client-Name': clientName || ''
                },
                origin: clientName || ''
            };
            // Create a new WebSocket with options
            this.ws = new WebSocket(wsUrl, options);
            const connectionTimeout = setTimeout(() => {
                if (this.ws && (this.ws.readyState === WebSocket.CONNECTING)) {
                    this.logger.warn('Connection timeout, terminating WebSocket');
                    this.disconnect();
                    reject(new McpUnityError(ErrorType.CONNECTION, 'Connection timeout'));
                }
            }, this.REQUEST_TIMEOUT);
            this.ws.onopen = () => {
                clearTimeout(connectionTimeout);
                this.logger.debug('WebSocket connected');
                resolve();
            };
            this.ws.onerror = (err) => {
                clearTimeout(connectionTimeout);
                this.logger.error(`WebSocket error: ${err.message || 'Unknown error'}`);
                reject(new McpUnityError(ErrorType.CONNECTION, `Connection failed: ${err.message || 'Unknown error'}`));
                this.disconnect();
            };
            this.ws.onmessage = (event) => {
                this.handleMessage(event.data.toString());
            };
            this.ws.onclose = () => {
                this.logger.debug('WebSocket closed');
                this.disconnect();
            };
        });
    }
    /**
     * Handle messages received from Unity
     */
    handleMessage(data) {
        try {
            const response = JSON.parse(data);
            if (response.id && this.pendingRequests.has(response.id)) {
                const request = this.pendingRequests.get(response.id);
                clearTimeout(request.timeout);
                this.pendingRequests.delete(response.id);
                if (response.error) {
                    request.reject(new McpUnityError(ErrorType.TOOL_EXECUTION, response.error.message || 'Unknown error', response.error.details));
                }
                else {
                    request.resolve(response.result);
                }
            }
        }
        catch (e) {
            this.logger.error(`Error parsing WebSocket message: ${e instanceof Error ? e.message : String(e)}`);
        }
    }
    /**
     * Disconnect from Unity
     */
    disconnect() {
        if (this.ws) {
            this.logger.debug(`Disconnecting WebSocket in state: ${this.ws.readyState}`);
            // First remove all event handlers to prevent callbacks during close
            this.ws.onopen = null;
            this.ws.onmessage = null;
            this.ws.onerror = null;
            this.ws.onclose = null;
            // Different handling based on WebSocket state
            try {
                if (this.ws.readyState === WebSocket.CONNECTING) {
                    // For sockets still connecting, use terminate() to force immediate close
                    this.ws.terminate();
                }
                else if (this.ws.readyState === WebSocket.OPEN) {
                    // For open sockets, use close() for clean shutdown
                    this.ws.close();
                }
            }
            catch (err) {
                this.logger.error(`Error closing WebSocket: ${err instanceof Error ? err.message : String(err)}`);
            }
            // Clear the reference
            this.ws = null;
            // Reject all pending requests
            for (const [id, request] of this.pendingRequests.entries()) {
                clearTimeout(request.timeout);
                request.reject(new McpUnityError(ErrorType.CONNECTION, 'Connection closed'));
                this.pendingRequests.delete(id);
            }
        }
    }
    /**
     * Tries to reconnect to Unity
     */
    reconnect() {
        this.disconnect();
        this.connect();
    }
    /**
     * Stop the Unity connection
     */
    async stop() {
        this.disconnect();
        this.logger.info('Unity WebSocket client stopped');
        return Promise.resolve();
    }
    /**
     * Send a request to the Unity server
     */
    async sendRequest(request) {
        // Ensure we're connected first
        if (!this.isConnected) {
            this.logger.info('Not connected to Unity, connecting first...');
            await this.connect();
        }
        // Use given id or generate a new one
        const requestId = request.id || uuidv4();
        const message = {
            ...request,
            id: requestId
        };
        return new Promise((resolve, reject) => {
            // Double check isConnected again after await
            if (!this.ws || !this.isConnected) {
                reject(new McpUnityError(ErrorType.CONNECTION, 'Not connected to Unity'));
                return;
            }
            // Create timeout for the request
            const timeout = setTimeout(() => {
                if (this.pendingRequests.has(requestId)) {
                    this.logger.error(`Request ${requestId} timed out after ${this.REQUEST_TIMEOUT}ms`);
                    this.pendingRequests.delete(requestId);
                    reject(new McpUnityError(ErrorType.TIMEOUT, 'Request timed out'));
                }
                this.reconnect();
            }, this.REQUEST_TIMEOUT);
            // Store pending request
            this.pendingRequests.set(requestId, {
                resolve,
                reject,
                timeout
            });
            try {
                this.ws.send(JSON.stringify(message));
                this.logger.debug(`Request sent: ${requestId}`);
            }
            catch (err) {
                clearTimeout(timeout);
                this.pendingRequests.delete(requestId);
                reject(new McpUnityError(ErrorType.CONNECTION, `Send failed: ${err instanceof Error ? err.message : String(err)}`));
            }
        });
    }
    /**
     * Check if connected to Unity
     * Only returns true if the connection is guaranteed to be active
     */
    get isConnected() {
        // Basic WebSocket connection check
        return this.ws !== null && this.ws.readyState === WebSocket.OPEN;
    }
    /**
     * Retrieves the UNITY_PORT value from the Windows registry (HKCU\Environment)
     * @returns The port value as a string if found, otherwise an empty string
     */
    getUnityPortFromWindowsRegistry() {
        const regKey = new winreg({ hive: winreg.HKCU, key: '\\Environment' });
        let result = '';
        regKey.get('UNITY_PORT', (err, item) => {
            if (err) {
                this.logger.error(`Error getting registry value: ${err.message}`);
            }
            else {
                result = item.value;
            }
        });
        return result;
    }
    /**
     * Retrieves the UNITY_PORT value from Unix-like system environment variables
     * @returns The port value as a string if found, otherwise an empty string
     */
    getUnityPortFromUnixRegistry() {
        return execSync('printenv UNITY_PORT', { stdio: ['pipe', 'pipe', 'ignore'] }).toString().trim();
    }
}
