# DBMigrator CLI v0.5.0-beta

**PostgreSQL Migration Tool - MVP 5 (Enterprise Ready)**

Advanced PostgreSQL database migration tool with automatic change detection, enterprise features, and multi-database support.

## Features Overview

### ✅ Core Features (MVP 1-2)
- PostgreSQL connection with validation
- Migration history tracking with checksums
- Apply SQL migrations with transaction safety
- Automatic change detection and migration generation
- Baseline management for schema snapshots
- Schema comparison and diff reports
- Rollback support with DOWN scripts
- Multi-environment configuration

### ✅ Team Collaboration (MVP 3)
- **Dry Run Mode** - Simulate migrations without applying
- **Conflict Detection** - Detect and resolve migration conflicts
- **Migration Listing** - Advanced filtering (applied/pending)
- **Configuration Management** - Multi-environment setup

### ✅ Production Ready (MVP 4)
- **SQL Validation** - Syntax checking and security validation
- **Backup Management** - Automated backups before migrations
- **Repair Tools** - Fix checksums, locks, and recovery
- **Migration Verification** - Integrity checks and validation

### ✅ Enterprise Ready (MVP 5)
- **Multi-Database Clusters** - Manage multiple databases simultaneously
- **Performance Metrics** - Monitoring and performance tracking
- **Deployment Orchestration** - Automated deployment pipelines
- **Interactive Shell** - Command-line interface with autocomplete
- **Advanced Logging** - Structured logging with different levels

## Prerequisites

- .NET 8.0
- PostgreSQL 13+

## Installation

```bash
cd src/DBMigrator.CLI
dotnet build
```

## Quick Start

### 1. Configuration

Option A - Environment Variable:
```bash
export DB_CONNECTION="Host=localhost;Database=myapp;Username=dev;Password=pass"
```

Option B - Configuration File (`dbmigrator.json`):
```json
{
  "connectionString": "Host=localhost;Database=myapp;Username=dev;Password=pass",
  "migrationsPath": "./migrations",
  "environment": "development",
  "schema": "public"
}
```

### 2. Initialize and Create Baseline

```bash
# Initialize migration system
dotnet run -- init

# Create baseline snapshot of current schema
dotnet run -- baseline create
```

### 3. Auto-Generate Migrations

```bash
# Make changes to your database (via pgAdmin, psql, etc.)
# Then detect and generate migration:
dotnet run -- create --auto
```

### 4. Apply Migrations

```bash
# Apply generated migrations
dotnet run -- apply ./migrations/[generated-file].up.sql
```

## Commands Reference

### Core Commands
- `init` - Initialize migration history table
- `apply <file.sql>` - Apply a migration file
- `status` - Show applied migrations
- `create [--auto] [--name <name>]` - Create migration (auto-detect or manual)
- `baseline <action>` - Manage baseline (create, show)
- `diff` - Show differences between baseline and current schema
- `down [--count <n>]` - Rollback n migrations

### Team Collaboration (MVP 3)
- `dry-run <file.sql>` - Simulate migration without applying
- `check-conflicts` - Detect migration conflicts
- `list [--applied|--pending]` - List migrations with filtering
- `config <action>` - Manage configuration (init, show, env)

### Production Ready (MVP 4)
- `validate [file.sql]` - Validate SQL syntax and safety
- `backup <action>` - Create and manage database backups (create, list, cleanup)
- `repair <action>` - Repair checksums, locks, and recover
- `verify <action>` - Verify migration integrity

### Enterprise Ready (MVP 5)
- `cluster <action>` - Multi-database cluster management (register, list, health, apply, status)
- `metrics <action>` - Performance monitoring and metrics (show, system, export, clear)
- `deploy <action>` - Automated deployment orchestration (plan, execute, validate, status, rollback)
- `interactive` / `shell` - Start interactive shell mode

### Global Options
- `--env <environment>` - Use specific environment configuration
- `--help`, `-h` - Show help message

## Workflow Examples

### Auto-Detection Workflow
```bash
# Setup
export DB_CONNECTION="Host=localhost;Database=testdb;Username=postgres"
dotnet run -- init
dotnet run -- baseline create

# Make database changes via SQL client:
# CREATE TABLE products (id SERIAL PRIMARY KEY, name VARCHAR(200));

# Detect and generate migration
dotnet run -- create --auto
# Output: Created migration: ./migrations/20241127120000_auto_create_products.up.sql

# Review changes
dotnet run -- diff

# Apply migration
dotnet run -- apply ./migrations/20241127120000_auto_create_products.up.sql

# Check status
dotnet run -- status
```

### Manual Migration Workflow
```bash
# Create manual migration template
dotnet run -- create --name "add_user_indexes"

# Edit the generated file with your SQL
# Apply when ready
dotnet run -- apply ./migrations/[generated-file].sql
```

### Enterprise Features Workflow
```bash
# Dry run before applying
dotnet run -- dry-run ./migrations/migration.sql

# Validate SQL safety
dotnet run -- validate ./migrations/migration.sql

# Create backup before applying
dotnet run -- backup create --type full

# Check for conflicts
dotnet run -- check-conflicts

# Apply with verification
dotnet run -- apply ./migrations/migration.sql
dotnet run -- verify checksums
```

### Multi-Database Cluster Management
```bash
# Register databases in cluster
dotnet run -- cluster register --name prod-db --connection "Host=prod;Database=app"
dotnet run -- cluster register --name staging-db --connection "Host=staging;Database=app"

# Apply to multiple databases
dotnet run -- cluster apply migration.sql --databases prod-db,staging-db

# Monitor cluster health
dotnet run -- cluster health
```

### Deployment Orchestration
```bash
# Plan deployment
dotnet run -- deploy plan --name release-v1.2 --strategy parallel

# Execute deployment
dotnet run -- deploy execute --plan release-v1.2

# Monitor metrics
dotnet run -- metrics show
```

### Rollback Workflow
```bash
# Rollback last migration
dotnet run -- down

# Rollback multiple migrations
dotnet run -- down --count 3

# Emergency rollback via deployment
dotnet run -- deploy rollback --plan release-v1.2
```

## Architecture (Enterprise Ready)

```
src/
├── DBMigrator.CLI/              # CLI application
│   ├── Commands/                # All command implementations
│   │   ├── InitCommand.cs       # Initialize migration system
│   │   ├── ApplyCommand.cs      # Apply migrations
│   │   ├── StatusCommand.cs     # Show migration status
│   │   ├── CreateCommand.cs     # Migration creation with auto-detection
│   │   ├── BaselineCommand.cs   # Baseline management
│   │   ├── DiffCommand.cs       # Schema comparison
│   │   ├── DownCommand.cs       # Rollback support
│   │   ├── DryRunCommand.cs     # MVP 3: Simulation mode
│   │   ├── CheckConflictsCommand.cs # MVP 3: Conflict detection
│   │   ├── ListCommand.cs       # MVP 3: Migration listing
│   │   ├── ConfigCommand.cs     # MVP 3: Configuration management
│   │   ├── ValidateCommand.cs   # MVP 4: SQL validation
│   │   ├── BackupCommand.cs     # MVP 4: Backup management
│   │   ├── RepairCommand.cs     # MVP 4: Repair tools
│   │   ├── VerifyCommand.cs     # MVP 4: Verification
│   │   ├── ClusterCommand.cs    # MVP 5: Multi-DB cluster management
│   │   ├── MetricsCommand.cs    # MVP 5: Performance metrics
│   │   └── DeployCommand.cs     # MVP 5: Deployment orchestration
│   ├── Interactive/             # MVP 5: Interactive shell
│   │   └── InteractiveShell.cs
│   └── Program.cs               # Main CLI entry point
└── DBMigrator.Core/            # Core functionality
    ├── Models/                 # Data models
    │   ├── Migration.cs        # Migration representation
    │   ├── GeneratedMigration.cs # Auto-generated migrations
    │   ├── Configuration.cs    # Configuration models
    │   ├── DryRun/            # Dry run results
    │   ├── Schema/            # Schema representation (Tables, Columns, Indexes, Functions)
    │   ├── Changes/           # Change detection (TableChanges, ColumnChange, DatabaseChanges)
    │   ├── Conflicts/         # Conflict detection models
    │   └── Configuration/     # Configuration management
    ├── Services/              # Core services
    │   ├── MigrationService.cs      # Core migration logic with validation
    │   ├── SchemaAnalyzer.cs        # Schema analysis
    │   ├── ChangeDetector.cs        # Change detection
    │   ├── MigrationGenerator.cs    # SQL generation
    │   ├── ConfigurationManager.cs  # Multi-environment config
    │   ├── ConflictDetector.cs      # MVP 3: Conflict detection
    │   ├── DryRunExecutor.cs        # MVP 3: Simulation
    │   ├── MigrationValidator.cs    # MVP 4: SQL validation
    │   ├── BackupManager.cs         # MVP 4: Backup management
    │   ├── ChecksumManager.cs       # MVP 4: Integrity verification
    │   ├── MigrationLockManager.cs  # MVP 4: Concurrent access control
    │   ├── DeploymentManager.cs     # MVP 5: Deployment orchestration
    │   ├── MultiDatabaseManager.cs  # MVP 5: Cluster management
    │   ├── MetricsCollector.cs      # MVP 5: Performance monitoring
    │   ├── TransactionManager.cs    # Transaction safety
    │   ├── ConnectionStringValidator.cs # Connection validation
    │   ├── ColumnChangeDetector.cs  # Advanced change detection
    │   ├── AlterTableGenerator.cs   # DDL generation
    │   └── StructuredLogger.cs      # Advanced logging
    └── Database/              # Database access
        └── ConnectionManager.cs     # PostgreSQL connection management
```

## Generated Migration Structure

```
./migrations/
├── .baseline.json                           # Schema baseline
├── 20241127120000_auto_create_products.up.sql    # UP script
├── 20241127120000_auto_create_products.down.sql  # DOWN script
└── 20241127121500_manual_add_indexes.sql         # Manual migration
```

## Configuration Options

### dbmigrator.json
```json
{
  "connectionString": "",                    // DB connection (env var override)
  "migrationsPath": "./migrations",          // Migration files location
  "environment": "development",              // Environment name
  "schema": "public",                        // Database schema
  "autoGenerateDown": true,                  // Generate DOWN scripts
  "createBackupBeforeMigration": true,       // Auto-backup before apply
  "backupPath": "./backups",                 // Backup location
  "commandTimeout": 30,                      // SQL command timeout
  "verboseOutput": false                     // Verbose logging
}
```

### Environment Variables
- `DB_CONNECTION` - PostgreSQL connection string (overrides config)
- `MIGRATOR_ENVIRONMENT` - Environment name
- `MIGRATOR_MIGRATIONS_PATH` - Migrations directory
- `MIGRATOR_SCHEMA` - Database schema name
- `MIGRATOR_LOG_LEVEL` - Logging level (Debug, Info, Warning, Error)
- `MIGRATOR_BACKUP_PATH` - Backup directory path
- `MIGRATOR_COMMAND_TIMEOUT` - SQL command timeout in seconds

## Troubleshooting

### No baseline found
```bash
# Error: No baseline found. Create one with 'dbmigrator baseline create'
dotnet run -- baseline create
```

### Connection issues
```bash
# Test connection
dotnet run -- status
```

### View current differences
```bash
# See what changes would be detected
dotnet run -- diff
```

## Enterprise Features Implemented

### 🔧 Advanced Management
- **Multi-Database Clusters** - Manage multiple PostgreSQL instances simultaneously
- **Deployment Orchestration** - Automated deployment pipelines with rollback support
- **Performance Monitoring** - Built-in metrics collection and analysis
- **Interactive Shell** - Advanced CLI with autocomplete and help

### 🛡️ Production Safety
- **SQL Security Validation** - Detects potentially dangerous SQL patterns
- **Transaction Safety** - All migrations run in transactions with proper rollback
- **Checksum Verification** - Ensures migration integrity and detects tampering
- **Backup Integration** - Automated backups before applying migrations

### 👥 Team Collaboration
- **Conflict Detection** - Identifies conflicting migrations across team members
- **Dry Run Mode** - Simulate migrations without applying changes
- **Environment Management** - Support for dev/staging/production configurations
- **Structured Logging** - Detailed logging for debugging and auditing

### 🔍 Schema Analysis
- **Auto-Detection** - Automatically detects tables, columns, indexes, and functions
- **Smart Generation** - Creates both UP and DOWN migration scripts
- **Change Detection** - Identifies schema differences with precision
- **Visual Diffs** - Clear reporting of schema changes

## Dependencies & Technology Stack

- **.NET 8.0** - Modern C# with latest features
- **Npgsql 8.0.4** - PostgreSQL .NET driver
- **Microsoft.Extensions.Configuration** - Configuration management
- **System.Text.Json** - JSON serialization
- **SHA256 Checksums** - Migration integrity verification
- **Structured Logging** - Advanced logging capabilities