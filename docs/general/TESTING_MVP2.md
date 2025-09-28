# Guía de Testing MVP 2

## 🔧 Problema Solucionado

El error **"A command is already in progress"** se ha corregido. El problema era que los `NpgsqlDataReader` no se cerraban correctamente antes de ejecutar nuevos comandos en la misma conexión.

**Solución aplicada:**
- Envolvimos todos los comandos SQL en bloques `using` apropiados
- Separamos la consulta de tablas de las consultas de columnas/índices
- Aseguramos que cada reader se cierre antes del siguiente comando

## 🧪 Comandos de Prueba

### 1. Verificar Conexión Básica
```bash
export DB_CONNECTION="Host=localhost;Database=testdb;Username=postgres;Password=yourpass"
dotnet run -- help
```

### 2. Inicializar Sistema
```bash
# Inicializar tabla de migraciones
dotnet run -- init
```

### 3. Crear Baseline (Corregido)
```bash
# Crear snapshot del schema actual
dotnet run -- baseline create
```

### 4. Ver Baseline
```bash
# Mostrar información del baseline
dotnet run -- baseline show
```

### 5. Test de Detección de Cambios
```bash
# 1. Crear baseline inicial
dotnet run -- baseline create

# 2. Hacer cambios en BD (via psql/pgAdmin):
# CREATE TABLE products (
#     id SERIAL PRIMARY KEY,
#     name VARCHAR(200) NOT NULL,
#     price DECIMAL(10,2)
# );

# 3. Ver diferencias
dotnet run -- diff

# 4. Generar migración automática
dotnet run -- create --auto

# 5. Ver archivos generados
ls -la migrations/
```

### 6. Test de Migración Manual
```bash
# Crear migración manual
dotnet run -- create --name "add_user_indexes"

# Ver archivo generado
cat migrations/*manual*
```

### 7. Test de Estado
```bash
# Ver migraciones aplicadas
dotnet run -- status
```

## 🔍 Solución de Problemas

### Error: "A command is already in progress"
✅ **SOLUCIONADO** - Ya no debería aparecer este error.

### Error: "No password provided"
```bash
# Asegúrate de incluir password en connection string:
export DB_CONNECTION="Host=localhost;Database=testdb;Username=postgres;Password=yourpass"
```

### Error: "No baseline found"
```bash
# Crear baseline primero:
dotnet run -- baseline create
```

### Error: Connection refused
```bash
# Verificar que PostgreSQL esté corriendo:
sudo systemctl status postgresql
# o
brew services list | grep postgres
```

## 📋 Flujo de Prueba Completo

```bash
#!/bin/bash
# Script de prueba completo

echo "🧪 Testing DBMigrator MVP 2"

# 1. Configuración
export DB_CONNECTION="Host=localhost;Database=test_migrator;Username=postgres;Password=yourpass"

# 2. Verificar help
echo "📚 Testing help command..."
dotnet run -- help

# 3. Inicializar
echo "🚀 Initializing system..."
dotnet run -- init

# 4. Crear baseline
echo "📸 Creating baseline..."
dotnet run -- baseline create

# 5. Mostrar baseline
echo "👀 Showing baseline..."
dotnet run -- baseline show

# 6. Crear migración manual
echo "✍️ Creating manual migration..."
dotnet run -- create --name "test_migration"

# 7. Ver diferencias (sin cambios)
echo "🔍 Checking differences..."
dotnet run -- diff

# 8. Ver estado
echo "📊 Checking status..."
dotnet run -- status

echo "✅ All tests completed!"
```

## 🎯 Casos de Uso Reales

### Caso 1: Agregar Nueva Tabla
```bash
# 1. Crear baseline
dotnet run -- baseline create

# 2. En psql/pgAdmin, ejecutar:
# CREATE TABLE categories (
#     id SERIAL PRIMARY KEY,
#     name VARCHAR(100) UNIQUE NOT NULL,
#     description TEXT
# );

# 3. Detectar cambios
dotnet run -- diff

# 4. Generar migración
dotnet run -- create --auto

# 5. Aplicar migración
dotnet run -- apply ./migrations/*auto*up.sql
```

### Caso 2: Modificar Tabla Existente
```bash
# 1. En psql/pgAdmin, agregar columna:
# ALTER TABLE users ADD COLUMN email VARCHAR(255);

# 2. Detectar cambios
dotnet run -- diff

# 3. Generar migración
dotnet run -- create --auto --name "add_user_email"
```

### Caso 3: Rollback
```bash
# Rollback última migración
dotnet run -- down

# Rollback múltiples migraciones
dotnet run -- down --count 3
```

## 📁 Estructura de Archivos Generados

Después de las pruebas, deberías ver:
```
./migrations/
├── .baseline.json                    # Snapshot del schema
├── 20241127xxxxxx_auto_*.up.sql     # Script UP generado
├── 20241127xxxxxx_auto_*.down.sql   # Script DOWN generado
└── 20241127xxxxxx_manual_*.sql      # Migración manual

./dbmigrator.json                     # Configuración (opcional)
```

## ⚡ Testing Rápido

Para una prueba rápida sin BD real:
```bash
# Solo verificar que compila y muestra ayuda
dotnet run -- help

# Verificar configuración
dotnet run -- status  # Mostrará error de conexión pero no de código
```

El error de conexión en este caso confirma que el código funciona correctamente, solo falta la BD configurada.