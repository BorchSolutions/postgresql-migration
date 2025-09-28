-- Test script to verify function detection works correctly
-- Run this to test the function detection after the fix

-- Simple function without problematic types
CREATE OR REPLACE FUNCTION test_simple(param1 integer)
RETURNS integer
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN param1 * 2;
END;
$$;

-- Function with char type (the problematic one)
CREATE OR REPLACE FUNCTION test_with_char(param1 "char")
RETURNS text
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN 'Char value: ' || param1;
END;
$$;

-- Function with mixed parameter types
CREATE OR REPLACE FUNCTION test_mixed_params(
    param1 integer,
    param2 text DEFAULT 'default',
    param3 boolean
)
RETURNS text
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN format('Values: %s, %s, %s', param1, param2, param3);
END;
$$;

-- Function with no parameters
CREATE OR REPLACE FUNCTION test_no_params()
RETURNS timestamp
LANGUAGE plpgsql
AS $$
BEGIN
    RETURN now();
END;
$$;