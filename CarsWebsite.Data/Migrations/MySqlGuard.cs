namespace cars_website_api.Migrations
{
    // MySQL (unlike MariaDB) has no IF [NOT] EXISTS clause for ADD/DROP COLUMN, CREATE/DROP INDEX
    // or DROP FOREIGN KEY, so idempotent migrations must check INFORMATION_SCHEMA and PREPARE the
    // statement dynamically. Same approach 20260623200000_FixMissingForeignKeys already used -
    // extracted here because several later migrations regressed to the MariaDB-only shorthand and
    // silently failed on production MySQL (Database.Migrate is wrapped in a non-fatal catch).
    internal static class MySqlGuard
    {
        public static string AddColumnIfMissing(string table, string column, string definition) => $@"
            SET @guard_exists = (
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'
            );
            SET @guard_sql = IF(@guard_exists = 0,
                'ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}',
                'SELECT 1');
            PREPARE guard_stmt FROM @guard_sql;
            EXECUTE guard_stmt;
            DEALLOCATE PREPARE guard_stmt;";

        public static string DropColumnIfExists(string table, string column) => $@"
            SET @guard_exists = (
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'
            );
            SET @guard_sql = IF(@guard_exists > 0,
                'ALTER TABLE `{table}` DROP COLUMN `{column}`',
                'SELECT 1');
            PREPARE guard_stmt FROM @guard_sql;
            EXECUTE guard_stmt;
            DEALLOCATE PREPARE guard_stmt;";

        // kind: "" (regular), "UNIQUE" or "FULLTEXT"
        public static string CreateIndexIfMissing(string table, string index, string columnsSql, string kind = "") => $@"
            SET @guard_exists = (
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND INDEX_NAME = '{index}'
            );
            SET @guard_sql = IF(@guard_exists = 0,
                'CREATE {(kind.Length > 0 ? kind + " " : "")}INDEX `{index}` ON `{table}` ({columnsSql})',
                'SELECT 1');
            PREPARE guard_stmt FROM @guard_sql;
            EXECUTE guard_stmt;
            DEALLOCATE PREPARE guard_stmt;";

        public static string DropIndexIfExists(string table, string index) => $@"
            SET @guard_exists = (
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND INDEX_NAME = '{index}'
            );
            SET @guard_sql = IF(@guard_exists > 0,
                'DROP INDEX `{index}` ON `{table}`',
                'SELECT 1');
            PREPARE guard_stmt FROM @guard_sql;
            EXECUTE guard_stmt;
            DEALLOCATE PREPARE guard_stmt;";

        public static string DropForeignKeyIfExists(string table, string constraint) => $@"
            SET @guard_exists = (
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'
                  AND CONSTRAINT_NAME = '{constraint}' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
            );
            SET @guard_sql = IF(@guard_exists > 0,
                'ALTER TABLE `{table}` DROP FOREIGN KEY `{constraint}`',
                'SELECT 1');
            PREPARE guard_stmt FROM @guard_sql;
            EXECUTE guard_stmt;
            DEALLOCATE PREPARE guard_stmt;";

        public static string AddForeignKeyIfMissing(string table, string constraint, string definition) => $@"
            SET @guard_exists = (
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}'
                  AND CONSTRAINT_NAME = '{constraint}' AND CONSTRAINT_TYPE = 'FOREIGN KEY'
            );
            SET @guard_sql = IF(@guard_exists = 0,
                'ALTER TABLE `{table}` ADD CONSTRAINT `{constraint}` {definition}',
                'SELECT 1');
            PREPARE guard_stmt FROM @guard_sql;
            EXECUTE guard_stmt;
            DEALLOCATE PREPARE guard_stmt;";
    }
}
