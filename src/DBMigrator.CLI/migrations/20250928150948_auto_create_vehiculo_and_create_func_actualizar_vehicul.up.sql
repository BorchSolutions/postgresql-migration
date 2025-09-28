-- Auto-generated migration
-- Generated at: 2025-09-28 15:09:48 UTC
-- Description: 1 new table(s), 5 new function(s)

-- Create new tables
CREATE TABLE public.vehiculo (
    id integer NOT NULL DEFAULT nextval('vehiculo_id_seq'::regclass),
    marca character varying NOT NULL,
    modelo character varying NOT NULL,
    anio integer,
    PRIMARY KEY (id)
);

-- Create new functions
-- Create function public.actualizar_vehiculo(integer, character varying, character varying, integer)
CREATE OR REPLACE FUNCTION public.actualizar_vehiculo(p_id integer, p_marca character varying, p_modelo character varying, p_anio integer)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
BEGIN
    UPDATE vehiculo
    SET
        marca = p_marca,
        modelo = p_modelo,
        anio = p_anio
    WHERE id = p_id;
END;
$function$


-- Create function public.crear_vehiculo(character varying, character varying, integer)
CREATE OR REPLACE FUNCTION public.crear_vehiculo(p_marca character varying, p_modelo character varying, p_anio integer)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO vehiculo (marca, modelo, anio)
    VALUES (p_marca, p_modelo, p_anio);
END;
$function$


-- Create function public.eliminar_vehiculo(integer)
CREATE OR REPLACE FUNCTION public.eliminar_vehiculo(p_id integer)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
BEGIN
    DELETE FROM vehiculo
    WHERE id = p_id;
END;
$function$


-- Create function public.obtener_todos_los_vehiculos()
CREATE OR REPLACE FUNCTION public.obtener_todos_los_vehiculos()
 RETURNS TABLE(id_vehiculo integer, marca_vehiculo character varying, modelo_vehiculo character varying, anio_vehiculo integer)
 LANGUAGE plpgsql
AS $function$
BEGIN
    RETURN QUERY
    SELECT id, marca, modelo, anio FROM vehiculo ORDER BY id;
END;
$function$


-- Create function public.obtener_vehiculo_por_id(integer)
CREATE OR REPLACE FUNCTION public.obtener_vehiculo_por_id(p_id integer)
 RETURNS TABLE(id_vehiculo integer, marca_vehiculo character varying, modelo_vehiculo character varying, anio_vehiculo integer)
 LANGUAGE plpgsql
AS $function$
BEGIN
    RETURN QUERY
    SELECT id, marca, modelo, anio FROM vehiculo WHERE id = p_id;
END;
$function$



