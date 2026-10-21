# Componentes de terceiros

OrbLink usa MIT para o código próprio (titular ainda a confirmar). Dependências mantêm suas licenças e copyrights; estes avisos acompanham o ZIP binário/source.

| Componente distribuído | Versão desta build | Licença/aviso |
| --- | --- | --- |
| .NET runtime e Windows Desktop/WPF | 10.0.12 | MIT .NET Foundation and Contributors; [licença](docs/licenses/DOTNET-LICENSE.txt), [avisos adicionais do runtime](docs/licenses/DOTNET-NOTICES.txt) |
| Microsoft.Data.Sqlite / Core | 10.0.12 | MIT, Microsoft Corporation/.NET Foundation and Contributors; mesma licença MIT acima |
| System.Security.Cryptography.ProtectedData | referência NuGet 10.0.0; runtime Windows Desktop 10.0.12 | MIT .NET Foundation and Contributors |
| SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 | 2.1.12 | Apache-2.0; Copyright 2014–2024 SourceGear, LLC; [licença](docs/licenses/SQLITEPCLRAW-LICENSE.txt) |

SQLite nativo é distribuído no pacote lib.e_sqlite3; os notices do wrapper/bundle não substituem os termos dos componentes. Consulte as fontes/metadata upstream para detalhes. Pacotes de testes (xUnit, Test SDK, coverlet) não são incluídos no ZIP executável; suas referências/versões continuam nos projetos de teste do source.

Textos de licença/notices foram copiados das versões de runtime do SDK/cache NuGet e do upstream SQLitePCLRaw v2.1.12, sem alterar permissões. Ao atualizar dependências, revise versões/avisos e preserve os arquivos correspondentes.

Fontes: [SQLitePCLRaw v2.1.12](https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.12/LICENSE.TXT), [.NET runtime](https://github.com/dotnet/runtime/blob/v10.0.12/LICENSE.TXT), [EF Core/Microsoft.Data.Sqlite](https://github.com/dotnet/efcore/blob/v10.0.12/LICENSE.txt).
