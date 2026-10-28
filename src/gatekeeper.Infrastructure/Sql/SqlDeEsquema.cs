namespace gatekeeper.Persistence.Adapters;

internal static class SqlDeEsquema
{
    public const string GarantirVersaoAnsi =
        """
        CREATE TABLE IF NOT EXISTS esquema_versao (
            versao integer NOT NULL PRIMARY KEY
        )
        """;

    public const string GarantirVersaoSqlServer =
        """
        IF OBJECT_ID(N'esquema_versao', N'U') IS NULL
        BEGIN
            CREATE TABLE esquema_versao (
                versao int NOT NULL PRIMARY KEY
            );
        END
        """;

    public const string LerVersao = "SELECT versao FROM esquema_versao";

    public const string GravarVersao = "INSERT INTO esquema_versao (versao) VALUES (@versao)";

    public const string GarantirCaixaAnsi =
        """
        CREATE TABLE IF NOT EXISTS caixa_saida (
            id varchar(64) NOT NULL PRIMARY KEY,
            assunto varchar(200) NOT NULL,
            corpo varchar(4000) NOT NULL,
            publicada integer NOT NULL
        )
        """;

    public const string AlargarCaixaAnsi =
        """
        ALTER TABLE caixa_saida ALTER COLUMN id TYPE varchar(64)
        """;

    public const string AlargarCaixaSqlServer =
        """
        ALTER TABLE caixa_saida ALTER COLUMN id nvarchar(64) NOT NULL
        """;

    public const string AlargarCaixaMySql =
        """
        ALTER TABLE caixa_saida MODIFY id varchar(64) NOT NULL
        """;

    public const string GarantirEscritorioAnsi =
        """
        CREATE TABLE IF NOT EXISTS office_organization (
            id varchar(36) NOT NULL PRIMARY KEY,
            name varchar(200) NOT NULL,
            host varchar(200) NOT NULL,
            idp_issuer varchar(500) NOT NULL,
            directory_id varchar(200) NOT NULL,
            removed_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS office_plan (
            id varchar(36) NOT NULL PRIMARY KEY,
            name varchar(200) NOT NULL,
            price varchar(40) NOT NULL,
            seat_limit integer NOT NULL,
            removed_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS office_subscription (
            id varchar(36) NOT NULL PRIMARY KEY,
            organization_id varchar(36) NOT NULL,
            plan_id varchar(36) NOT NULL,
            starts_on varchar(40) NOT NULL,
            ends_on varchar(40) NOT NULL,
            removed_on varchar(40) NOT NULL
        )
        """;

    public const string GarantirEscritorioSqlServer =
        """
        IF OBJECT_ID(N'office_organization', N'U') IS NULL
        BEGIN
            CREATE TABLE office_organization (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                name nvarchar(200) NOT NULL,
                host nvarchar(200) NOT NULL,
                idp_issuer nvarchar(500) NOT NULL,
                directory_id nvarchar(200) NOT NULL,
                removed_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'office_plan', N'U') IS NULL
        BEGIN
            CREATE TABLE office_plan (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                name nvarchar(200) NOT NULL,
                price nvarchar(40) NOT NULL,
                seat_limit int NOT NULL,
                removed_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'office_subscription', N'U') IS NULL
        BEGIN
            CREATE TABLE office_subscription (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                organization_id nvarchar(36) NOT NULL,
                plan_id nvarchar(36) NOT NULL,
                starts_on nvarchar(40) NOT NULL,
                ends_on nvarchar(40) NOT NULL,
                removed_on nvarchar(40) NOT NULL
            );
        END
        """;

    public const string AcordarPrecoAnsi =
        """
        ALTER TABLE office_subscription ADD COLUMN agreed_price varchar(40) NOT NULL DEFAULT '0'
        """;

    public const string AcordarPrecoSqlServer =
        """
        IF COL_LENGTH(N'office_subscription', N'agreed_price') IS NULL
        ALTER TABLE office_subscription ADD agreed_price nvarchar(40) NOT NULL CONSTRAINT DF_office_subscription_agreed DEFAULT N'0'
        """;

    public const string GarantirOperacaoAnsi =
        """
        CREATE TABLE IF NOT EXISTS office_member (
            id varchar(36) NOT NULL PRIMARY KEY,
            organization_id varchar(36) NOT NULL,
            email varchar(200) NOT NULL,
            display_name varchar(200) NOT NULL,
            role varchar(64) NOT NULL,
            removed_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS office_machine_key (
            id varchar(36) NOT NULL PRIMARY KEY,
            organization_id varchar(36) NOT NULL,
            name varchar(200) NOT NULL,
            prefix varchar(16) NOT NULL,
            secret_hash varchar(64) NOT NULL,
            created_on varchar(40) NOT NULL,
            revoked_on varchar(40) NOT NULL,
            last_used_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS office_team (
            id varchar(36) NOT NULL PRIMARY KEY,
            organization_id varchar(36) NOT NULL,
            name varchar(200) NOT NULL,
            goal varchar(500) NOT NULL,
            updated_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS office_team_member (
            id varchar(36) NOT NULL PRIMARY KEY,
            team_id varchar(36) NOT NULL,
            name varchar(200) NOT NULL,
            contact varchar(200) NOT NULL,
            paper varchar(32) NOT NULL,
            responsibilities varchar(500) NOT NULL,
            state varchar(32) NOT NULL,
            invited_on varchar(40) NOT NULL,
            invited_by varchar(200) NOT NULL,
            invite_expires_on varchar(40) NOT NULL,
            accepted_on varchar(40) NOT NULL,
            expires_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS audit_chain (
            seq integer NOT NULL PRIMARY KEY,
            occurred_at varchar(40) NOT NULL,
            actor varchar(200) NOT NULL,
            action varchar(200) NOT NULL,
            resource varchar(200) NOT NULL,
            outcome varchar(200) NOT NULL,
            correlation varchar(200) NOT NULL,
            previous_hash varchar(64) NOT NULL,
            hash varchar(64) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS access_rule (
            id varchar(36) NOT NULL PRIMARY KEY,
            resource varchar(64) NOT NULL,
            role varchar(64) NOT NULL,
            action varchar(32) NOT NULL,
            effect varchar(16) NOT NULL,
            removed_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS process_mark (
            id varchar(36) NOT NULL PRIMARY KEY,
            bucket varchar(64) NOT NULL,
            mark_key varchar(400) NOT NULL,
            seen_on varchar(40) NOT NULL
        )
        """;

    public const string GarantirOperacaoSqlServer =
        """
        IF OBJECT_ID(N'office_member', N'U') IS NULL
        BEGIN
            CREATE TABLE office_member (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                organization_id nvarchar(36) NOT NULL,
                email nvarchar(200) NOT NULL,
                display_name nvarchar(200) NOT NULL,
                role nvarchar(64) NOT NULL,
                removed_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'office_machine_key', N'U') IS NULL
        BEGIN
            CREATE TABLE office_machine_key (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                organization_id nvarchar(36) NOT NULL,
                name nvarchar(200) NOT NULL,
                prefix nvarchar(16) NOT NULL,
                secret_hash nvarchar(64) NOT NULL,
                created_on nvarchar(40) NOT NULL,
                revoked_on nvarchar(40) NOT NULL,
                last_used_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'office_team', N'U') IS NULL
        BEGIN
            CREATE TABLE office_team (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                organization_id nvarchar(36) NOT NULL,
                name nvarchar(200) NOT NULL,
                goal nvarchar(500) NOT NULL,
                updated_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'office_team_member', N'U') IS NULL
        BEGIN
            CREATE TABLE office_team_member (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                team_id nvarchar(36) NOT NULL,
                name nvarchar(200) NOT NULL,
                contact nvarchar(200) NOT NULL,
                paper nvarchar(32) NOT NULL,
                responsibilities nvarchar(500) NOT NULL,
                state nvarchar(32) NOT NULL,
                invited_on nvarchar(40) NOT NULL,
                invited_by nvarchar(200) NOT NULL,
                invite_expires_on nvarchar(40) NOT NULL,
                accepted_on nvarchar(40) NOT NULL,
                expires_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'audit_chain', N'U') IS NULL
        BEGIN
            CREATE TABLE audit_chain (
                seq int NOT NULL PRIMARY KEY,
                occurred_at nvarchar(40) NOT NULL,
                actor nvarchar(200) NOT NULL,
                action nvarchar(200) NOT NULL,
                resource nvarchar(200) NOT NULL,
                outcome nvarchar(200) NOT NULL,
                correlation nvarchar(200) NOT NULL,
                previous_hash nvarchar(64) NOT NULL,
                hash nvarchar(64) NOT NULL
            );
        END
        IF OBJECT_ID(N'access_rule', N'U') IS NULL
        BEGIN
            CREATE TABLE access_rule (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                resource nvarchar(64) NOT NULL,
                role nvarchar(64) NOT NULL,
                action nvarchar(32) NOT NULL,
                effect nvarchar(16) NOT NULL,
                removed_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'process_mark', N'U') IS NULL
        BEGIN
            CREATE TABLE process_mark (
                id nvarchar(36) NOT NULL PRIMARY KEY,
                bucket nvarchar(64) NOT NULL,
                mark_key nvarchar(400) NOT NULL,
                seen_on nvarchar(40) NOT NULL
            );
        END
        """;

    public const string GarantirCaixaSqlServer =
        """
        IF OBJECT_ID(N'caixa_saida', N'U') IS NULL
        BEGIN
            CREATE TABLE caixa_saida (
                id nvarchar(64) NOT NULL PRIMARY KEY,
                assunto nvarchar(200) NOT NULL,
                corpo nvarchar(4000) NOT NULL,
                publicada int NOT NULL
            );
        END
        """;


    public const string GarantirEspacosAnsi =
        """
        CREATE TABLE IF NOT EXISTS ws_scope (
            id varchar(80) NOT NULL PRIMARY KEY,
            kind varchar(12) NOT NULL,
            parent_id varchar(80) NOT NULL,
            name varchar(200) NOT NULL,
            subject varchar(500) NOT NULL,
            created_on varchar(40) NOT NULL,
            created_by varchar(200) NOT NULL,
            removed_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS ws_doc (
            workspace_id varchar(80) NOT NULL,
            collection varchar(40) NOT NULL,
            id varchar(200) NOT NULL,
            version integer NOT NULL,
            body text NOT NULL,
            updated_on varchar(40) NOT NULL,
            updated_by varchar(200) NOT NULL,
            PRIMARY KEY (workspace_id, collection, id)
        );
        CREATE TABLE IF NOT EXISTS ws_collection (
            workspace_id varchar(80) NOT NULL,
            collection varchar(40) NOT NULL,
            version integer NOT NULL,
            PRIMARY KEY (workspace_id, collection)
        );
        CREATE TABLE IF NOT EXISTS ws_member (
            workspace_id varchar(80) NOT NULL,
            member_id varchar(200) NOT NULL,
            team_id varchar(200) NOT NULL,
            kind varchar(10) NOT NULL,
            contact varchar(300) NOT NULL,
            role varchar(40) NOT NULL,
            state varchar(20) NOT NULL,
            expires_on varchar(40) NOT NULL,
            PRIMARY KEY (workspace_id, member_id)
        )
        """;

    public const string GarantirEspacosMySql =
        """
        CREATE TABLE IF NOT EXISTS ws_scope (
            id varchar(80) NOT NULL PRIMARY KEY,
            kind varchar(12) NOT NULL,
            parent_id varchar(80) NOT NULL,
            name varchar(200) NOT NULL,
            subject varchar(500) NOT NULL,
            created_on varchar(40) NOT NULL,
            created_by varchar(200) NOT NULL,
            removed_on varchar(40) NOT NULL
        );
        CREATE TABLE IF NOT EXISTS ws_doc (
            workspace_id varchar(80) NOT NULL,
            collection varchar(40) NOT NULL,
            id varchar(200) NOT NULL,
            version integer NOT NULL,
            body longtext NOT NULL,
            updated_on varchar(40) NOT NULL,
            updated_by varchar(200) NOT NULL,
            PRIMARY KEY (workspace_id, collection, id)
        );
        CREATE TABLE IF NOT EXISTS ws_collection (
            workspace_id varchar(80) NOT NULL,
            collection varchar(40) NOT NULL,
            version integer NOT NULL,
            PRIMARY KEY (workspace_id, collection)
        );
        CREATE TABLE IF NOT EXISTS ws_member (
            workspace_id varchar(80) NOT NULL,
            member_id varchar(200) NOT NULL,
            team_id varchar(200) NOT NULL,
            kind varchar(10) NOT NULL,
            contact varchar(300) NOT NULL,
            role varchar(40) NOT NULL,
            state varchar(20) NOT NULL,
            expires_on varchar(40) NOT NULL,
            PRIMARY KEY (workspace_id, member_id)
        )
        """;

    public const string GarantirEspacosSqlServer =
        """
        IF OBJECT_ID(N'ws_scope', N'U') IS NULL
        BEGIN
            CREATE TABLE ws_scope (
                id nvarchar(80) NOT NULL PRIMARY KEY,
                kind nvarchar(12) NOT NULL,
                parent_id nvarchar(80) NOT NULL,
                name nvarchar(200) NOT NULL,
                subject nvarchar(500) NOT NULL,
                created_on nvarchar(40) NOT NULL,
                created_by nvarchar(200) NOT NULL,
                removed_on nvarchar(40) NOT NULL
            );
        END
        IF OBJECT_ID(N'ws_doc', N'U') IS NULL
        BEGIN
            CREATE TABLE ws_doc (
                workspace_id nvarchar(80) NOT NULL,
                collection nvarchar(40) NOT NULL,
                id nvarchar(200) NOT NULL,
                version int NOT NULL,
                body nvarchar(max) NOT NULL,
                updated_on nvarchar(40) NOT NULL,
                updated_by nvarchar(200) NOT NULL,
                PRIMARY KEY (workspace_id, collection, id)
            );
        END
        IF OBJECT_ID(N'ws_collection', N'U') IS NULL
        BEGIN
            CREATE TABLE ws_collection (
                workspace_id nvarchar(80) NOT NULL,
                collection nvarchar(40) NOT NULL,
                version int NOT NULL,
                PRIMARY KEY (workspace_id, collection)
            );
        END
        IF OBJECT_ID(N'ws_member', N'U') IS NULL
        BEGIN
            CREATE TABLE ws_member (
                workspace_id nvarchar(80) NOT NULL,
                member_id nvarchar(200) NOT NULL,
                team_id nvarchar(200) NOT NULL,
                kind nvarchar(10) NOT NULL,
                contact nvarchar(300) NOT NULL,
                role nvarchar(40) NOT NULL,
                state nvarchar(20) NOT NULL,
                expires_on nvarchar(40) NOT NULL,
                PRIMARY KEY (workspace_id, member_id)
            );
        END
        """;
}
