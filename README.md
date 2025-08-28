# 🚀 BorchSolutions PostgreSQL Migration Tool

Herramienta profesional para migraciones de bases de datos PostgreSQL diseñada para la gestión completa del ciclo de vida de esquemas y datos.

## ✨ Características Principales

### 🏗️ **Gestión Completa de Migraciones**
- ✅ Migraciones de esquema (tablas, funciones, triggers, índices)
- ✅ Migraciones de datos maestros con scripts idempotentes
- ✅ Generación automática de baseline desde BD existentes
- ✅ Control de versiones y checksums de integridad
- ✅ Transacciones automáticas con rollback

### 🔧 **Control de Cambios Avanzado**
- ✅ Tracking de archivos de migración en múltiples rutas
- ✅ Detección automática de cambios (agregados, modificados, eliminados)
- ✅ Control global de directorios de migración
- ✅ Validación de integridad de archivos

### 🌐 **Multi-Base de Datos**
- ✅ Soporte para múltiples conexiones simultáneas
- ✅ Configuración centralizada de ambientes (Dev, Staging, Prod)
- ✅ Ejecución paralela o secuencial según necesidades

### 🛡️ **Seguridad y Robustez**
- ✅ Validaciones de permisos antes de ejecutar
- ✅ Modo dry-run para verificación previa
- ✅ Logging detallado con diferentes niveles
- ✅ Manejo de errores con contexto completo

---

## 📋 Requisitos

- **.NET 8.0** o superior
- **PostgreSQL 12+**
- Permisos de **CREATE**, **INSERT**, **UPDATE** en la base de datos objetivo

---

## ⚙️ Instalación y Configuración

### 1. **Configurar Connection Strings**

Editar `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=your_db;Port=5432;User Id=user;Password=pass;",
    "TargetDatabases": {
      "Development": "Server=localhost;Database=dev_db;Port=5432;User Id=dev_user;Password=dev_pass;",
      "Staging": "Server=staging-server;Database=staging_db;Port=5432;User Id=staging_user;Password=staging_pass;",
      "Production": "Server=prod-server;Database=prod_db;Port=5432;User Id=prod_user;Password=prod_pass;"
    }
  },
  "MigrationSettings": {
    "SchemaTable": "borchsolutions_schema_migrations",
    "DataTable": "borchsolutions_data_migrations",
    "MigrationsPath": "Migrations",
    "SchemaPath": "Schema",
    "DataPath": "Data",
    "EnableTransactions": true,
    "EnableBackups": true,
    "CommandTimeout": 300
  }
}
```

### 2. **Instalar y Compilar**

```bash
dotnet restore
dotnet build
```

### 3. **Inicializar Motor de Migraciones**

```bash
dotnet run init
```

---

## 🎯 Comandos Disponibles

### **🔧 Inicialización**

```bash
# Inicializar motor de migraciones (crear tablas de control)
dotnet run init [--connection <name>]
```

### **📊 Información y Estado**

```bash
# Mostrar información de conexiones disponibles
dotnet run info [--connection <name>] [--verbose]

# Ver estado de migraciones
dotnet run status [--connection <name>] [--verbose]

# Validar integridad de migraciones ejecutadas
dotnet run validate [--connection <name>]
```

### **🏗️ Baseline (Para Bases de Datos Existentes)**

```bash
# Generar baseline desde BD existente
dotnet run baseline generate [--connection <name>] [--output <path>]

# Marcar BD actual como baseline ejecutado
dotnet run baseline mark [--connection <name>]
```

### **🚀 Ejecutar Migraciones**

```bash
# Ejecutar migraciones pendientes
dotnet run migrate [--connection <name>] [--dry-run] [--verbose]

# Solo verificar migraciones pendientes (sin ejecutar)
dotnet run migrate --dry-run [--connection <name>]
```

### **🗃️ Gestión de Datos**

```bash
# Extraer datos de tablas específicas
dotnet run data extract --tables "table1,table2,table3" [--output <path>] [--connection <name>]

# Verificar existencia y contenido de tablas
dotnet run data test --tables "table1,table2" [--connection <name>]
```

### **📁 Control de Cambios**

```bash
# Inicializar control de cambios en directorio
dotnet run control init --path "/path/to/migrations"

# Escanear cambios en directorio controlado
dotnet run control scan --path "/path/to/migrations" [--verbose]

# Listar todos los directorios bajo control
dotnet run control list [--verbose]

# Remover directorio del control
dotnet run control remove --path "/path/to/migrations"
```

---

## 📁 Estructura de Proyecto

```
BorchSolutions.PostgreSQL.Migration/
├── 📁 Migrations/
│   ├── 📁 Schema/                    # Scripts de estructura
│   │   ├── V001_001__Create_Users_Table.sql
│   │   ├── V001_002__Add_Index_Users_Email.sql
│   │   └── V002_001__Create_Products_Table.sql
│   └── 📁 Data/                     # Scripts de datos
│       ├── D001_001__Initial_User_Roles.sql
│       ├── D001_002__Countries_Data.sql
│       └── D002_001__Product_Categories.sql
├── 📁 Backups/                      # Backups automáticos (opcional)
├── appsettings.json                 # Configuración
└── .borchsolutions-migration-control # Control de cambios (auto-generado)
```

### **Convenciones de Naming**

#### Scripts de Estructura:
```
V{Major}_{Minor}__{Description}.sql

Ejemplos:
V001_001__Create_Users_Table.sql
V001_002__Add_Index_Users_Email.sql
V002_001__Add_Notifications_Feature.sql
```

#### Scripts de Datos:
```
D{Major}_{Minor}__{Description}.sql

Ejemplos:
D001_001__Initial_User_Roles.sql
D001_002__Countries_Data.sql
D002_001__Product_Categories.sql
```

---

## 🎯 Flujos de Trabajo Típicos

### **🆕 Para Proyecto Nuevo (Base de Datos Vacía)**

```bash
# 1. Inicializar motor
dotnet run init

# 2. Crear scripts de estructura en Migrations/Schema/
# Ejemplo: V001_001__Create_Initial_Tables.sql

# 3. Crear scripts de datos en Migrations/Data/
# Ejemplo: D001_001__Initial_Master_Data.sql

# 4. Verificar migraciones pendientes
dotnet run status

# 5. Ejecutar migraciones
dotnet run migrate
```

### **🔄 Para Base de Datos Existente**

```bash
# 1. Inicializar motor
dotnet run init

# 2. Generar baseline desde BD existente
dotnet run baseline generate --output "Migrations/Schema/V000_001__Initial_Baseline.sql"

# 3. Marcar baseline como ejecutado
dotnet run baseline mark

# 4. Extraer datos maestros existentes (opcional)
dotnet run data extract --tables "roles,permissions,countries" --output "Migrations/Data/D000_001__Existing_Data.sql"

# 5. A partir de aquí, workflow normal para nuevos cambios
dotnet run migrate --dry-run
```

### **🔄 Desarrollo Continuo**

```bash
# 1. Crear nuevos scripts cuando sea necesario
# V002_001__Add_New_Feature.sql
# D002_001__New_Master_Data.sql

# 2. Verificar qué está pendiente
dotnet run status

# 3. Ejecutar migraciones
dotnet run migrate --dry-run  # Verificar primero
dotnet run migrate             # Ejecutar

# 4. Validar integridad
dotnet run validate
```

### **🚀 Deploy a Producción**

```bash
# 1. Verificar estado actual
dotnet run status --connection Production

# 2. Dry run para confirmar cambios
dotnet run migrate --connection Production --dry-run

# 3. Ejecutar migraciones reales
dotnet run migrate --connection Production

# 4. Validar resultado
dotnet run validate --connection Production
```

---

## 💡 Ejemplos Prácticos

### **📋 Ejemplo: Script de Estructura**

`Migrations/Schema/V001_001__Create_Users_Table.sql`:

```sql
-- Crear tabla de usuarios
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    is_email_verified BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);

-- Índices para optimización
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
CREATE INDEX IF NOT EXISTS idx_users_verified ON users(is_email_verified) 
WHERE is_email_verified = true;
```

### **🗂️ Ejemplo: Script de Datos**

`Migrations/Data/D001_001__User_Roles.sql`:

```sql
-- Insertar roles básicos del sistema
INSERT INTO user_roles (id, name, description, is_system_role, created_at) 
VALUES 
    (1, 'SuperAdmin', 'Administrador del sistema con acceso completo', true, NOW()),
    (2, 'Admin', 'Administrador con permisos de gestión', true, NOW()),
    (3, 'User', 'Usuario estándar del sistema', true, NOW()),
    (4, 'Guest', 'Usuario invitado con permisos limitados', true, NOW())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    updated_at = NOW();

-- Resetear la secuencia si es necesario
SELECT SETVAL('user_roles_id_seq', COALESCE((SELECT MAX(id) FROM user_roles), 1));
```

---

## 🔧 Control de Cambios Avanzado

### **Inicializar Control en Directorio**

```bash
# Inicializar control de cambios
dotnet run control init --path "/path/to/migrations"

# El sistema creará:
# - Archivo .borchsolutions-migration-control en el directorio
# - Registro en ~/.borchsolutions/migration-control.json
```

### **Detectar Cambios**

```bash
# Escanear cambios en directorio
dotnet run control scan --path "/path/to/migrations" --verbose

# Salida esperada:
# 📊 Resumen de cambios:
#   ➕ Agregados: 2
#   📝 Modificados: 1
#   🗑️  Eliminados: 0
#   ✅ Sin cambios: 15
```

### **Gestión Multi-Proyecto**

```bash
# Listar todos los proyectos bajo control
dotnet run control list --verbose

# 📋 Directorios bajo control de cambios:
# 📁 /proyecto1/migrations
#    Archivos: 23
#    Último escaneo: 2025-08-28 15:30:00
#    Estado: ✅ Íntegro
# 
# 📁 /proyecto2/migrations  
#    Archivos: 45
#    Último escaneo: 2025-08-28 14:20:00
#    Estado: ⚠️  Con cambios
```

---

## 🛡️ Mejores Prácticas

### **✅ Recomendaciones**
- ✅ Siempre usar `--dry-run` antes de ejecutar en producción
- ✅ Mantener scripts idempotentes con `ON CONFLICT` 
- ✅ Probar migraciones en ambiente de staging primero
- ✅ Usar control de cambios para detectar modificaciones no autorizadas
- ✅ Validar integridad regularmente con `dotnet run validate`
- ✅ Hacer backup antes de migraciones importantes
- ✅ Separar claramente estructura de datos maestros

### **❌ Evitar**
- ❌ Modificar scripts ya ejecutados
- ❌ Usar `DROP TABLE` sin verificaciones extensas
- ❌ Scripts sin `WHERE` en `DELETE/UPDATE` masivos
- ❌ Hardcodear IDs sin `ON CONFLICT`
- ❌ Ejecutar en producción sin dry-run previo

---

## 🧪 Testing y Validación

### **Verificación Rápida de Tablas**

```bash
# Verificar existencia y contenido
dotnet run data test --tables "users,roles,permissions"

# Salida:
# 🧪 Verificando 3 tablas para conexión: Default
#   ✅ users: 1,247 registros
#   ✅ roles: 4 registros  
#   ✅ permissions: 23 registros
# ✅ Verificación completada
```

### **Validación de Integridad**

```bash
# Validar checksums y consistencia
dotnet run validate --verbose

# Verifica:
# - Checksums de scripts vs registros en BD
# - Versiones duplicadas
# - Referencias faltantes
```

---

## 📚 Referencia Rápida de Comandos

```bash
# INICIALIZACIÓN
dotnet run init                                         # Inicializar motor de migraciones

# INFORMACIÓN
dotnet run info --verbose                              # Mostrar conexiones y estado de BD
dotnet run status --connection Production             # Estado de migraciones por conexión

# BASELINE  
dotnet run baseline generate --output baseline.sql    # Generar baseline desde BD existente
dotnet run baseline mark                               # Marcar BD como baseline

# MIGRACIONES
dotnet run migrate --dry-run                          # Verificar migraciones pendientes  
dotnet run migrate --connection Staging               # Ejecutar migraciones en Staging

# DATOS
dotnet run data extract --tables "tabla1,tabla2"     # Extraer datos de tablas
dotnet run data test --tables "tabla1,tabla2"        # Verificar tablas

# CONTROL DE CAMBIOS
dotnet run control init --path "./Migrations"         # Inicializar control
dotnet run control scan --path "./Migrations"         # Escanear cambios
dotnet run control list --verbose                     # Listar paths controlados

# VALIDACIÓN
dotnet run validate --connection Production           # Validar integridad
```

---

## 🤝 Soporte

### **Logs Detallados**
Usar `--verbose` en cualquier comando para información detallada de ejecución.

### **Configuración de Logging**
En `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "BorchSolutions": "Debug"
    }
  }
}
```

### **Troubleshooting Común**
- **Error de conexión**: Verificar connection string y permisos
- **Scripts no encontrados**: Verificar estructura de directorios
- **Error de permisos**: Usuario BD debe tener permisos CREATE/INSERT/UPDATE

---

## 📄 Licencia

© 2025 **BorchSolutions**. Todos los derechos reservados.

---

## 🎉 ¡Listo para Usar!

Con **BorchSolutions PostgreSQL Migration Tool** tienes control total sobre la evolución de tu base de datos, con:

- 🚀 **Migraciones automatizadas** de esquema y datos
- 🔍 **Control de cambios** en tiempo real  
- 🛡️ **Validaciones de integridad** continuas
- 🌐 **Soporte multi-base** de datos
- 📊 **Reportes detallados** de estado

**¡Happy coding!** ✨

---

*Versión: 1.0.0 | Compatibilidad: .NET 8.0+ / PostgreSQL 12+ | Última actualización: Agosto 2025*