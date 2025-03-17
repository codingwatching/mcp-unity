// Import MCP SDK components
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { readFileSync, existsSync } from 'fs';
import { join, dirname } from 'path';
import { McpUnity } from './unity/mcpUnity.js';
import { Logger, LogLevel } from './utils/logger.js';
import { ToolRegistry } from './tools/toolRegistry.js';
import { createMenuItemTool } from './tools/menuItemTool.js';
// Initialize loggers
const serverLogger = new Logger('Server', LogLevel.INFO);
const unityLogger = new Logger('Unity', LogLevel.INFO);
const toolLogger = new Logger('Tools', LogLevel.INFO);
// Read port from port.txt file or use default
function getPort() {
    // Get the directory where this script is located and go up one level
    let portFilePath = join(dirname(new URL(import.meta.url).pathname), '..', '..', 'port.txt');
    if (portFilePath.startsWith('\\')) {
        portFilePath = portFilePath.substring(1);
    }
    try {
        if (existsSync(portFilePath)) {
            const portStr = readFileSync(portFilePath, 'utf-8').trim();
            const port = parseInt(portStr, 10);
            if (!isNaN(port) && port > 0 && port < 65536) {
                serverLogger.info(`Using port from port.txt: ${port}`);
                return port;
            }
        }
    }
    catch (error) {
        serverLogger.warn(`Error reading port.txt: ${error}`);
    }
    serverLogger.info(`Could not find port.txt in path ${portFilePath}, using default port: 8090`);
    return 8090;
}
// Initialize the MCP server
const server = new McpServer({
    name: "MCP Unity Server",
    version: "1.0.0"
}, {
    capabilities: {
        tools: {},
    },
});
// Initialize Unity WebSocket bridge with port from port.txt
const mcpUnity = new McpUnity(getPort(), unityLogger);
// Initialize tool registry
const toolRegistry = new ToolRegistry(toolLogger);
// Add the menu item tool
toolRegistry.add(createMenuItemTool(mcpUnity, toolLogger));
// Register all tools with the MCP server
toolRegistry.registerWithServer(server);
// Server startup function
async function startServer() {
    try {
        // Initialize STDIO transport for MCP client communication
        const stdioTransport = new StdioServerTransport();
        // Connect the server to the transport
        await server.connect(stdioTransport);
        // Start Unity WebSocket connection
        await mcpUnity.start();
        serverLogger.info('MCP Server started and ready');
    }
    catch (error) {
        serverLogger.error('Failed to start server', error);
        process.exit(1);
    }
}
// Start the server
startServer();
// Handle shutdown
process.on('SIGINT', async () => {
    serverLogger.info('Shutting down...');
    await mcpUnity.stop();
    process.exit(0);
});
// Handle uncaught exceptions
process.on('uncaughtException', (error) => {
    serverLogger.error('Uncaught exception', error);
});
// Handle unhandled promise rejections
process.on('unhandledRejection', (reason) => {
    serverLogger.error('Unhandled rejection', reason);
});
