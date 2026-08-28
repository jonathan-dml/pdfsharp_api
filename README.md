# PDFSharp API

API HTTP para operações comuns em arquivos PDF usando ASP.NET Core e PdfSharpCore.

## Requisitos

- .NET 10 SDK

## Executar a API

Na pasta do projeto:

```powershell
dotnet run
```

Por padrão, a API fica disponível em:

- `http://localhost:5257`
- `https://localhost:7145`

A documentação OpenAPI fica disponível em ambiente de desenvolvimento em `/openapi/v1.json`.

## Formato das requisições

Os endpoints que recebem arquivos usam `multipart/form-data`.

O nome do campo do arquivo precisa corresponder ao nome indicado em cada endpoint. Arrays de inteiros são enviados como campos repetidos, por exemplo:

```text
selectedPages=2
selectedPages=4
```

Dicionários são enviados como uma string JSON em um campo do formulário, por exemplo:

```json
{"1":90,"2":180}
```

Os números das páginas começam em `1`.

## Endpoints

### GET `/`

Verifica se a API está disponível.

```powershell
curl http://localhost:5257/
```

Resposta:

```text
Toolkit for working with PDF files.
```

### POST `/merge`

Combina dois PDFs na ordem recebida e retorna `merged.pdf`.

Campos:

- `file1`: primeiro PDF
- `file2`: segundo PDF

```powershell
curl -X POST http://localhost:5257/merge `
  -F "file1=@first.pdf" `
  -F "file2=@second.pdf" `
  -o merged.pdf
```

### POST `/split`

Divide um PDF em vários PDFs agrupando as páginas conforme o dicionário `pageGroups`. Páginas com o mesmo grupo são colocadas no mesmo arquivo. A resposta é um ZIP chamado `split-pdfs.zip`.

Campos:

- `file`: PDF de origem
- `pageGroups`: objeto JSON em que a chave é o número da página e o valor é o grupo

Exemplo:

```json
{"1":1,"2":2,"3":1,"4":2}
```

```powershell
curl -X POST http://localhost:5257/split `
  -F "file=@document.pdf" `
  -F 'pageGroups={"1":1,"2":2,"3":1,"4":2}' `
  -o split-pdfs.zip
```

O exemplo cria `group-1.pdf` com as páginas 1 e 3, e `group-2.pdf` com as páginas 2 e 4.

### POST `/extract`

Cria `extracted.pdf` contendo somente as páginas selecionadas, na ordem informada.

Campos:

- `file`: PDF de origem
- `selectedPages`: números das páginas, enviados como campos repetidos

```powershell
curl -X POST http://localhost:5257/extract `
  -F "file=@document.pdf" `
  -F "selectedPages=3" `
  -F "selectedPages=1" `
  -F "selectedPages=5" `
  -o extracted.pdf
```

### POST `/delete`

Remove as páginas selecionadas e retorna `deleted-pages-removed.pdf` com as páginas restantes na ordem original.

Campos:

- `file`: PDF de origem
- `selectedPages`: números das páginas a remover, enviados como campos repetidos

```powershell
curl -X POST http://localhost:5257/delete `
  -F "file=@document.pdf" `
  -F "selectedPages=2" `
  -F "selectedPages=4" `
  -o deleted-pages-removed.pdf
```

Pelo menos uma página precisa permanecer no documento.

### POST `/rotate`

Gira as páginas indicadas e retorna `rotated.pdf`. Páginas que não aparecem no dicionário permanecem sem alteração.

Campos:

- `file`: PDF de origem
- `rotationAngles`: objeto JSON em que a chave é o número da página e o valor é o ângulo

Os ângulos aceitos são `0`, `90`, `180` e `270` graus.

```powershell
curl -X POST http://localhost:5257/rotate `
  -F "file=@document.pdf" `
  -F 'rotationAngles={"1":90,"3":180}' `
  -o rotated.pdf
```

### POST `/reorder`

Reordena todas as páginas e retorna `reordered.pdf`.

Campos:

- `file`: PDF de origem
- `newOrder`: objeto JSON em que a chave é o número original da página e o valor é sua nova posição

Exemplo:

```json
{"1":3,"2":1,"3":2}
```

O resultado será: página 2, página 3, página 1.

```powershell
curl -X POST http://localhost:5257/reorder `
  -F "file=@document.pdf" `
  -F 'newOrder={"1":3,"2":1,"3":2}' `
  -o reordered.pdf
```

`newOrder` precisa mapear todas as páginas exatamente uma vez, e cada nova posição precisa ser única.

### POST `/copy`

Copia páginas de `fileToCopy`, anexando-as ao final de `targetFile`, e retorna `copied-pages.pdf`.

Campos:

- `fileToCopy`: PDF de origem das páginas
- `targetFile`: PDF que receberá as páginas
- `selectedPages`: páginas de `fileToCopy` a copiar, enviadas como campos repetidos

```powershell
curl -X POST http://localhost:5257/copy `
  -F "fileToCopy=@source.pdf" `
  -F "targetFile=@target.pdf" `
  -F "selectedPages=2" `
  -F "selectedPages=4" `
  -o copied-pages.pdf
```

As páginas são anexadas na ordem em que aparecem nos campos `selectedPages`.

## Validação e erros

Os endpoints rejeitam, entre outras situações:

- Arquivo ausente, vazio ou que não seja um PDF válido
- Números de página menores que `1`
- Números de página duplicados nos arrays `selectedPages`
- Números de página fora do intervalo do PDF
- Dicionários JSON ausentes ou inválidos
- Mapeamentos incompletos ou posições duplicadas em `newOrder`
- Ângulos diferentes de `0`, `90`, `180` ou `270` em `rotationAngles`
- Tentativa de remover todas as páginas em `/delete`

As falhas de validação retornam HTTP `400 Bad Request` com uma mensagem explicando o problema.

