-- Auto-generated rollback migration
-- Generated at: 2025-09-28 15:09:48 UTC

-- Drop new tables DROP TABLE IF EXISTS public.vehiculo;
(in reverse order)
-- Drop new functions (in reverse order)
DROP FUNCTION IF EXISTS public.actualizar_vehiculo(integer, character varying, character varying, integer);
DROP FUNCTION IF EXISTS public.crear_vehiculo(character varying, character varying, integer);
DROP FUNCTION IF EXISTS public.eliminar_vehiculo(integer);
DROP FUNCTION IF EXISTS public.obtener_todos_los_vehiculos();
DROP FUNCTION IF EXISTS public.obtener_vehiculo_por_id(integer);

