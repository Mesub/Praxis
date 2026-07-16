-- The oracle's database (M14.1). Separate from `praxis` on purpose: the .NET core's connection
-- string points at `praxis`, and Postgres has no cross-database queries — so the answer keys
-- filed here are unreachable from the core even in principle (C-5). The AI plane owns this
-- database and creates its own tables in it.
CREATE DATABASE praxis_oracle;
