# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DBMigrator is a PostgreSQL database migration tool built with .NET 8 that has evolved through 5 MVP phases from basic migration management to enterprise-ready deployment automation. The tool supports multiple databases, interactive shell operations, automated backups, conflict detection, and deployment orchestration.

## Build and Development Commands

### Core Development Commands
```bash
# Build the entire solution
dotnet build

# Run the CLI tool from source
dotnet run -- <command> [options]

# Build and run in release mode
dotnet build -c Release
dotnet run -c Release -- <command>

# Clean build artifacts
dotnet clean
```

### Configuration Setup
```bash
# Set database connection (required)
export DB_CONNECTION="Host=localhost;Database=myapp;Username=postgres;Password=your_password"

# Use specific environment configuration
dotnet run -- <command> --env production
```

### Testing the Application
```bash
# Test basic functionality
dotnet run -- --help
dotnet run -- init
dotnet run -- status

# Test interactive mode
dotnet run -- interactive
# Or use alias:
dotnet run -- shell

# Test enterprise features
dotnet run -- metrics show
dotnet run -- cluster list
dotnet run -- deploy plan --name test-plan
```

## Architecture Overview

### Project Structure
- **DBMigrator.Core**: Core business logic, services, and models
- **DBMigrator.CLI**: Command-line interface, commands, and interactive shell

### Key Architectural Patterns

#### Service Layer Architecture
The application follows a service-oriented architecture with clear separation between:
- **ConnectionManager**: Database connection handling with connection pooling
- **TransactionManager**: Transaction lifecycle with savepoint support and recovery
- **MigrationService**: Core migration execution with SQL validation
- **ConfigurationManager**: Multi-environment configuration with JSON + environment variables

#### MVP Evolution Architecture
The codebase is organized around 5 MVP phases, each building upon the previous:
1. **MVP 1**: Basic migration apply/rollback
2. **MVP 2**: Auto-detection and migration generation  
3. **MVP 3**: Team collaboration (conflicts, dry-run, approvals)
4. **MVP 4**: Production features (validation, backup, checksum verification, locking)
5. **MVP 5**: Enterprise features (multi-database, metrics, deployment automation, interactive shell)

#### Multi-Database Enterprise Pattern
- **MultiDatabaseManager**: Orchestrates operations across database clusters
- **DeploymentManager**: Handles deployment strategies (Sequential, Parallel, Blue-Green, Canary)
- **MetricsCollector**: Performance monitoring with thread-safe collection and auto-flushing

#### Interactive Shell Architecture
- **InteractiveShell**: Full REPL with command completion, history, and wizards
- **Command Pattern**: All CLI commands implement consistent async execution pattern
- **Wizard System**: Guided workflows for setup, migration, and deployment

### Configuration System

#### Multi-Environment Support
Configuration is loaded in this precedence order:
1. Environment variables (`DB_CONNECTION`, `DBMIGRATOR_ENVIRONMENT`)
2. `dbmigrator.json` file with environment-specific sections
3. Default development settings

#### Security Considerations
- **Never commit passwords** to `dbmigrator.json` - use environment variables
- **SQL Injection Prevention**: All user inputs are validated, savepoint names use regex validation
- **Process Security**: External process calls use `ArgumentList` for safe argument passing
- **Credential Masking**: All logging automatically masks passwords and sensitive data

### Database Interaction Patterns

#### Migration Execution Flow
1. **Validation Phase**: SQL content validation, syntax checking, dangerous pattern detection
2. **Backup Phase**: Automatic backup creation (if enabled)
3. **Transaction Phase**: Execute within transaction with savepoints
4. **Verification Phase**: Checksum verification and integrity checks
5. **Recovery Phase**: Automatic rollback on failure with recovery logging

#### Locking and Concurrency
- **MigrationLockManager**: Prevents concurrent migrations with expiration-based locks
- **Savepoint Strategy**: Nested transaction support for granular rollback
- **Recovery System**: Failure tracking in `__dbmigrator_recovery_log` table

### Error Handling Strategy

#### Hierarchical Error Management
- **Command Level**: CLI commands catch and format user-friendly errors
- **Service Level**: Services log structured errors and handle business logic failures  
- **Database Level**: Connection and transaction errors with automatic retry logic
- **Validation Level**: Input validation with security-focused error messages

#### Recovery Mechanisms
- **Transaction Rollback**: Automatic rollback on migration failure
- **Backup Restoration**: Integration with backup system for disaster recovery
- **Lock Recovery**: Automatic cleanup of expired locks
- **Checksum Repair**: Tools for fixing checksum mismatches

## Key Integration Points

### External Dependencies
- **Npgsql 8.0.4**: PostgreSQL connectivity with async support
- **System.Text.Json 8.0.5**: Configuration and metrics serialization
- **Microsoft.Extensions.Configuration 8.0.0**: Multi-source configuration loading

### Database Schema Dependencies
- `__migrations`: Migration history tracking
- `__dbmigrator_schema_migrations`: Schema version management  
- `__dbmigrator_locks`: Concurrency control
- `__dbmigrator_recovery_log`: Failure tracking and recovery

### File System Dependencies
- `./migrations/`: Migration SQL files (timestamped naming)
- `./backups/`: Automatic backup storage
- `./logs/`: Structured logging output
- `dbmigrator.json`: Multi-environment configuration
- `.env`: Local environment variables (not committed)

## Security-Critical Areas

When modifying these areas, ensure security reviews:
- **TransactionManager.cs**: SQL injection prevention in savepoint operations
- **BackupManager.cs**: Process argument injection prevention  
- **MigrationService.cs**: SQL content validation and dangerous pattern detection
- **ConfigurationManager.cs**: Credential handling and sanitization
- **ConnectionStringValidator.cs**: Connection string security validation

Refer to `SECURITY.md` for detailed security guidelines and resolved vulnerabilities.

## Interactive Mode Usage

The interactive shell (`dotnet run -- interactive`) provides:
- **Command Completion**: Tab completion for all commands
- **History Navigation**: Arrow key navigation through command history
- **Guided Wizards**: Step-by-step setup, migration, and deployment assistance
- **Real-time Dashboard**: Live metrics and system status monitoring
- **Cross-platform Compatibility**: Automatic fallback for non-interactive environments