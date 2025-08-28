-- ========================================
-- EJEMPLO: MIGRACIÓN DE DATOS D001_001
-- Descripción: Datos maestros iniciales del sistema
-- Fecha: 2025-08-28
-- ========================================

-- ========================================
-- ROLES BÁSICOS DEL SISTEMA
-- ========================================

INSERT INTO user_roles (id, name, description, is_system_role, created_at) 
VALUES 
    (1, 'SuperAdmin', 'Administrador supremo con acceso completo al sistema', true, NOW()),
    (2, 'Admin', 'Administrador con permisos de gestión y configuración', true, NOW()),
    (3, 'Manager', 'Gestor con permisos de supervisión y reportes', true, NOW()),
    (4, 'User', 'Usuario estándar del sistema con permisos básicos', true, NOW()),
    (5, 'Guest', 'Usuario invitado con permisos de solo lectura limitados', true, NOW()),
    (6, 'ApiClient', 'Cliente de API con permisos específicos para integración', true, NOW())
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    is_system_role = EXCLUDED.is_system_role,
    updated_at = NOW();

-- Resetear secuencia para roles
SELECT SETVAL('user_roles_id_seq', COALESCE((SELECT MAX(id) FROM user_roles), 1));

-- ========================================
-- PERMISOS GRANULARES DEL SISTEMA
-- ========================================

-- Permisos para gestión de usuarios
INSERT INTO permissions (id, name, description, resource, action, created_at)
VALUES 
    (1, 'users.create', 'Crear nuevos usuarios en el sistema', 'users', 'create', NOW()),
    (2, 'users.read', 'Ver información de usuarios', 'users', 'read', NOW()),
    (3, 'users.update', 'Actualizar información de usuarios', 'users', 'update', NOW()),
    (4, 'users.delete', 'Eliminar usuarios del sistema', 'users', 'delete', NOW()),
    (5, 'users.list', 'Listar y buscar usuarios', 'users', 'list', NOW()),
    
    -- Permisos para gestión de roles
    (10, 'roles.create', 'Crear nuevos roles en el sistema', 'roles', 'create', NOW()),
    (11, 'roles.read', 'Ver información de roles', 'roles', 'read', NOW()),
    (12, 'roles.update', 'Actualizar información de roles', 'roles', 'update', NOW()),
    (13, 'roles.delete', 'Eliminar roles del sistema', 'roles', 'delete', NOW()),
    (14, 'roles.assign', 'Asignar roles a usuarios', 'roles', 'assign', NOW()),
    
    -- Permisos para gestión de permisos
    (20, 'permissions.read', 'Ver permisos disponibles', 'permissions', 'read', NOW()),
    (21, 'permissions.assign', 'Asignar permisos a roles', 'permissions', 'assign', NOW()),
    
    -- Permisos del sistema y configuración
    (30, 'system.config', 'Configurar parámetros del sistema', 'system', 'config', NOW()),
    (31, 'system.logs', 'Acceder a logs del sistema', 'system', 'logs', NOW()),
    (32, 'system.backup', 'Realizar backups del sistema', 'system', 'backup', NOW()),
    (33, 'system.maintenance', 'Realizar tareas de mantenimiento', 'system', 'maintenance', NOW()),
    
    -- Permisos de reportes
    (40, 'reports.view', 'Ver reportes del sistema', 'reports', 'view', NOW()),
    (41, 'reports.create', 'Crear nuevos reportes', 'reports', 'create', NOW()),
    (42, 'reports.export', 'Exportar reportes', 'reports', 'export', NOW()),
    
    -- Permisos de API
    (50, 'api.access', 'Acceso básico a la API', 'api', 'access', NOW()),
    (51, 'api.write', 'Operaciones de escritura via API', 'api', 'write', NOW()),
    (52, 'api.admin', 'Operaciones administrativas via API', 'api', 'admin', NOW())
    
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    resource = EXCLUDED.resource,
    action = EXCLUDED.action;

-- Resetear secuencia para permisos
SELECT SETVAL('permissions_id_seq', COALESCE((SELECT MAX(id) FROM permissions), 1));

-- ========================================
-- ASIGNACIÓN DE PERMISOS A ROLES
-- ========================================

-- SuperAdmin: Todos los permisos
INSERT INTO role_permissions (role_id, permission_id, granted_at, is_active)
SELECT 
    1 as role_id, 
    p.id as permission_id, 
    NOW() as granted_at,
    true as is_active
FROM permissions p
ON CONFLICT (role_id, permission_id) DO UPDATE SET
    is_active = EXCLUDED.is_active,
    granted_at = EXCLUDED.granted_at;

-- Admin: Permisos de gestión (sin system.backup y system.maintenance)
INSERT INTO role_permissions (role_id, permission_id, granted_at, is_active)
VALUES 
    -- Gestión de usuarios
    (2, 1, NOW(), true), (2, 2, NOW(), true), (2, 3, NOW(), true), (2, 5, NOW(), true),
    -- Gestión de roles (sin delete)
    (2, 10, NOW(), true), (2, 11, NOW(), true), (2, 12, NOW(), true), (2, 14, NOW(), true),
    -- Permisos
    (2, 20, NOW(), true), (2, 21, NOW(), true),
    -- Sistema (limitado)
    (2, 30, NOW(), true), (2, 31, NOW(), true),
    -- Reportes completos
    (2, 40, NOW(), true), (2, 41, NOW(), true), (2, 42, NOW(), true),
    -- API básica
    (2, 50, NOW(), true), (2, 51, NOW(), true)
ON CONFLICT (role_id, permission_id) DO UPDATE SET
    is_active = EXCLUDED.is_active,
    granted_at = EXCLUDED.granted_at;

-- Manager: Permisos de supervisión y reportes
INSERT INTO role_permissions (role_id, permission_id, granted_at, is_active)
VALUES 
    -- Ver usuarios
    (3, 2, NOW(), true), (3, 5, NOW(), true),
    -- Ver roles
    (3, 11, NOW(), true),
    -- Ver permisos
    (3, 20, NOW(), true),
    -- Logs del sistema
    (3, 31, NOW(), true),
    -- Reportes completos
    (3, 40, NOW(), true), (3, 41, NOW(), true), (3, 42, NOW(), true),
    -- API básica
    (3, 50, NOW(), true)
ON CONFLICT (role_id, permission_id) DO UPDATE SET
    is_active = EXCLUDED.is_active,
    granted_at = EXCLUDED.granted_at;

-- User: Permisos básicos
INSERT INTO role_permissions (role_id, permission_id, granted_at, is_active)
VALUES 
    -- Ver su propia información
    (4, 2, NOW(), true),
    -- Ver reportes básicos
    (4, 40, NOW(), true),
    -- API básica
    (4, 50, NOW(), true)
ON CONFLICT (role_id, permission_id) DO UPDATE SET
    is_active = EXCLUDED.is_active,
    granted_at = EXCLUDED.granted_at;

-- Guest: Solo lectura muy limitada
INSERT INTO role_permissions (role_id, permission_id, granted_at, is_active)
VALUES 
    -- Solo ver información básica
    (5, 2, NOW(), true)
ON CONFLICT (role_id, permission_id) DO UPDATE SET
    is_active = EXCLUDED.is_active,
    granted_at = EXCLUDED.granted_at;

-- ApiClient: Permisos específicos para API
INSERT INTO role_permissions (role_id, permission_id, granted_at, is_active)
VALUES 
    -- Acceso completo a API
    (6, 50, NOW(), true), (6, 51, NOW(), true),
    -- Lectura de usuarios y roles para validación
    (6, 2, NOW(), true), (6, 11, NOW(), true), (6, 20, NOW(), true)
ON CONFLICT (role_id, permission_id) DO UPDATE SET
    is_active = EXCLUDED.is_active,
    granted_at = EXCLUDED.granted_at;

-- ========================================
-- USUARIO ADMINISTRADOR INICIAL
-- ========================================

-- Crear usuario administrador por defecto (usar hash de contraseña real en producción)
INSERT INTO users (id, email, password_hash, first_name, last_name, is_email_verified, is_active, created_at)
VALUES (
    1,
    'admin@borchsolutions.com',
    '$2a$11$rQaGzOVJhI9FKwHHVmDd.OYGf5JFXyYmzFl8vqP0WvQDK3rM7WE7G', -- password: admin123 (cambiar en producción)
    'System',
    'Administrator',
    true,
    true,
    NOW()
)
ON CONFLICT (id) DO UPDATE SET
    email = EXCLUDED.email,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name,
    is_email_verified = EXCLUDED.is_email_verified,
    is_active = EXCLUDED.is_active,
    updated_at = NOW();

-- Resetear secuencia para usuarios
SELECT SETVAL('users_id_seq', COALESCE((SELECT MAX(id) FROM users), 1));

-- Asignar rol SuperAdmin al usuario administrador
INSERT INTO user_role_assignments (user_id, role_id, assigned_by, assigned_at, is_active)
VALUES (1, 1, 1, NOW(), true)
ON CONFLICT (user_id, role_id, is_active) DO UPDATE SET
    assigned_at = EXCLUDED.assigned_at,
    is_active = EXCLUDED.is_active;

-- ========================================
-- USUARIOS DE EJEMPLO (SOLO PARA DESARROLLO)
-- ========================================

-- NOTA: Estos usuarios solo deben existir en ambientes de desarrollo/staging
-- En producción, se deben crear a través de la interfaz del sistema

INSERT INTO users (id, email, password_hash, first_name, last_name, is_email_verified, is_active, created_at)
VALUES 
    (10, 'manager@example.com', '$2a$11$rQaGzOVJhI9FKwHHVmDd.OYGf5JFXyYmzFl8vqP0WvQDK3rM7WE7G', 'John', 'Manager', true, true, NOW()),
    (11, 'user@example.com', '$2a$11$rQaGzOVJhI9FKwHHVmDd.OYGf5JFXyYmzFl8vqP0WvQDK3rM7WE7G', 'Jane', 'User', true, true, NOW()),
    (12, 'guest@example.com', '$2a$11$rQaGzOVJhI9FKwHHVmDd.OYGf5JFXyYmzFl8vqP0WvQDK3rM7WE7G', 'Guest', 'Account', true, true, NOW())
ON CONFLICT (id) DO NOTHING; -- Solo insertar si no existen

-- Asignar roles a usuarios de ejemplo
INSERT INTO user_role_assignments (user_id, role_id, assigned_by, assigned_at, is_active)
VALUES 
    (10, 3, 1, NOW(), true), -- Manager role
    (11, 4, 1, NOW(), true), -- User role  
    (12, 5, 1, NOW(), true)  -- Guest role
ON CONFLICT (user_id, role_id, is_active) DO NOTHING; -- Solo insertar si no existen

-- ========================================
-- VALIDACIONES FINALES
-- ========================================

-- Verificar que se crearon los roles básicos
DO $$
DECLARE
    role_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO role_count FROM user_roles WHERE is_system_role = true;
    IF role_count < 6 THEN
        RAISE EXCEPTION 'Error: No se crearon todos los roles del sistema. Encontrados: %, Esperados: 6', role_count;
    END IF;
    RAISE NOTICE 'Verificación completada: % roles del sistema creados correctamente', role_count;
END $$;

-- Verificar que se crearon los permisos básicos  
DO $$
DECLARE
    perm_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO perm_count FROM permissions;
    IF perm_count < 15 THEN
        RAISE EXCEPTION 'Error: No se crearon suficientes permisos. Encontrados: %, Mínimo esperado: 15', perm_count;
    END IF;
    RAISE NOTICE 'Verificación completada: % permisos creados correctamente', perm_count;
END $$;

-- Verificar que el SuperAdmin tiene todos los permisos
DO $$
DECLARE
    admin_perms INTEGER;
    total_perms INTEGER;
BEGIN
    SELECT COUNT(*) INTO total_perms FROM permissions;
    SELECT COUNT(*) INTO admin_perms FROM role_permissions WHERE role_id = 1 AND is_active = true;
    
    IF admin_perms != total_perms THEN
        RAISE EXCEPTION 'Error: SuperAdmin no tiene todos los permisos. Asignados: %, Total: %', admin_perms, total_perms;
    END IF;
    RAISE NOTICE 'Verificación completada: SuperAdmin tiene % permisos asignados', admin_perms;
END $$;

-- ========================================
-- FIN DE MIGRACIÓN D001_001
-- ========================================