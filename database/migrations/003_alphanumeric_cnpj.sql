ALTER TABLE customers
    DROP CHECK ck_customers_cnpj,
    ADD CONSTRAINT ck_customers_cnpj CHECK (cnpj IS NULL OR cnpj REGEXP '^[A-Z0-9]{12}[0-9]{2}$');
