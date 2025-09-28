# PostgreSQL Compatibility Notes

## Version Compatibility

This migration tool is designed to work with PostgreSQL 10+ but has been specifically tested and optimized for PostgreSQL 15.x.

## PostgreSQL 15 Changes

### Function Detection (Fixed in this version)

**Issue:** The original query used `p.proisagg` which was removed in PostgreSQL 11+.

**Solution:** Use `p.prokind = 'f'` instead, which is the modern way to filter functions:

```sql
-- OLD (PostgreSQL 10 and earlier)
WHERE p.prokind = 'f' AND NOT p.proisagg

-- NEW (PostgreSQL 11+)
WHERE p.prokind = 'f'
```

### Function Types by `prokind`:
- `'f'` = Normal function
- `'p'` = Procedure  
- `'a'` = Aggregate function
- `'w'` = Window function

## Version-Specific Features

### PostgreSQL 15.x
- ✅ Functions fully supported
- ✅ Procedures supported via `prokind = 'p'`
- ✅ Advanced function properties (volatility, security definer)
- ✅ Function arguments parsing with `pg_get_function_arguments()`

### PostgreSQL 14.x
- ✅ Functions fully supported
- ✅ Most features compatible

### PostgreSQL 11-13.x
- ✅ Functions supported
- ⚠️ Some advanced features may need testing

### PostgreSQL 10.x
- ⚠️ Limited testing
- ⚠️ May require additional compatibility fixes

## Tested Configurations

| PostgreSQL Version | Status | Notes |
|-------------------|---------|-------|
| 15.4 (Debian) | ✅ Fully Tested | Primary development target |
| 14.x | ✅ Compatible | Expected to work |
| 13.x | ⚠️ Should work | Needs testing |
| 12.x | ⚠️ Should work | Needs testing |
| 11.x | ⚠️ Should work | Needs testing |
| 10.x | ❓ Unknown | May need fixes |

## Future Extensions

The function detection system is designed to be extensible for:
- Stored procedures (`prokind = 'p'`)
- Aggregate functions (`prokind = 'a'`)
- Window functions (`prokind = 'w'`)
- Triggers and trigger functions
- Custom data types
- Extensions and their objects

## Error Handling

If you encounter PostgreSQL version-specific errors:

1. **Check PostgreSQL Version:**
   ```sql
   SELECT version();
   ```

2. **Common Error Patterns:**
   - `column "proisagg" does not exist` → ✅ Fixed in this version
   - `column "prokind" does not exist` → PostgreSQL too old (< 11)
   - `Reading as 'System.String' is not supported for fields having DataTypeName 'char'` → ✅ Fixed with robust parameter parsing
   - Function definition parsing errors → Check `pg_get_functiondef()` availability

3. **Data Type Handling:**
   - **PostgreSQL `char` type**: Automatically mapped to `character` to avoid parsing issues
   - **Quoted identifiers**: Automatically stripped from parameter names and types
   - **Malformed parameters**: Skipped with warnings, operation continues
   - **Unknown types**: Mapped to `unknown` type with warning

4. **Fallback Strategy:**
   The system will attempt to continue operation even if some advanced function detection fails:
   - Parameter parsing failures → Function added with empty parameter list + warning
   - Individual parameter failures → Parameter skipped + warning  
   - Type parsing issues → Type marked as `unknown` + warning
   - Complete function failures → Function skipped + warning

5. **Robust Error Recovery:**
   - Functions with parsing issues are still detected and tracked
   - Migration generation continues even with incomplete function metadata
   - Warnings are displayed but don't stop the migration process

## Contributing

When adding new PostgreSQL-specific features:
1. Always check version compatibility
2. Use system catalogs that are stable across versions
3. Provide fallbacks for older versions
4. Test with multiple PostgreSQL versions when possible