# Arcanum — Arquitetura de Segurança

## Primitivas criptográficas

| Componente | Tecnologia | Motivo da escolha |
|---|---|---|
| KDF | Argon2id (RFC 9106) | Vencedor do Password Hashing Competition — resistente a GPU e ASIC |
| Cifra | AES-256-GCM | AEAD — confidencialidade + autenticação em uma operação |
| Expansão de chave | HKDF-SHA256 | Separa master_key da enc_key — defesa em profundidade |
| Aleatoriedade | `RandomNumberGenerator` (.NET) | CSPRNG nativo do sistema operacional — sem implementação caseira |

**Parâmetros Argon2id** (OWASP 2024, perfil interativo):
```
memória:     64 MiB
iterações:   3
paralelismo: 4 threads
resultado:   ~0.5–1 segundo por tentativa
```

"Uso interativo" significa que o vault é desbloqueado por um humano digitando uma senha em tempo real — diferente de um servidor que autentica milhares de requisições por segundo sem ninguém esperando. O OWASP define dois perfis:

| Perfil | Contexto | Tempo aceitável |
|---|---|---|
| Interativo | Humano aguardando na tela | ~0,5–1 segundo |
| Não-interativo | Servidor, batch, API | vários segundos |

Os parâmetros são calibrados para ser lentos o suficiente para dificultar brute force, mas rápidos o suficiente para não irritar o usuário.

---

## Formato do arquivo `.arcanum`

```
Offset   Tamanho   Campo
──────────────────────────────────────────────────────
0        4 bytes   MAGIC "PVLT"
4        2 bytes   VERSION
6        1 byte    KDF_ID (0x01 = Argon2id)
7        12 bytes  KDF_PARAMS (m_cost, t_cost, p_cost)
19       32 bytes  SALT aleatório
51       12 bytes  NONCE AES-GCM aleatório
63       8 bytes   PAYLOAD_LEN
71       N bytes   CIPHERTEXT + GCM TAG (16 bytes embutidos)
```

**O que cada campo significa:**

- **MAGIC** — primeiros 4 bytes sempre `PVLT`. O app confirma isso antes de tentar qualquer coisa, evitando tentar descriptografar um arquivo aleatório.
- **VERSION** — versão do formato. Reservado para mudanças futuras incompatíveis sem quebrar vaults antigos.
- **KDF_ID** — qual algoritmo derivou a chave (`0x01` = Argon2id). Permite suportar outros KDFs no futuro.
- **KDF_PARAMS** — os 3 parâmetros do Argon2id (memória, iterações, threads). Ficam no cabeçalho porque para descriptografar é preciso saber exatamente como a chave foi derivada.
- **SALT** — 32 bytes aleatórios gerados a cada salvamento. Entra no Argon2id junto com a senha mestre para gerar a chave. Duas pessoas com a mesma senha produzem chaves completamente diferentes.
- **NONCE** — 12 bytes aleatórios usados uma única vez pelo AES-GCM. Também renovado a cada save.
- **PAYLOAD_LEN** — tamanho em bytes do bloco criptografado, para saber até onde ler.
- **CIPHERTEXT + GCM TAG** — o JSON comprimido e criptografado. Os últimos 16 bytes são a tag de autenticação — se qualquer byte do arquivo for alterado, a tag não bate e o app rejeita tudo antes de expor qualquer dado.

**Por que o cabeçalho não é criptografado?**

O app precisa ler o salt e os parâmetros KDF *antes* de poder derivar a chave para descriptografar — é uma dependência circular. A solução é deixar o cabeçalho em aberto, mas **autenticado**: ele é passado como AAD (Additional Authenticated Data) para o AES-GCM, então qualquer adulteração nos parâmetros KDF também invalida a tag. Isso impede o downgrade attack — um atacante não consegue forçar parâmetros mais fracos sem que o app detecte.

**Por que comprimir antes de criptografar?**

Ciphertext AES-GCM é matematicamente indistinguível de ruído aleatório — ruído não tem padrões repetidos, então um compressor aplicado depois não consegue reduzir nada. O JSON, por outro lado, tem muita repetição (`"service"`, `"login"`, `"password"` em cada entrada). Comprimir primeiro e criptografar depois aproveita esses padrões e reduz o tamanho do arquivo no disco.

**Proteções do formato:**
- O cabeçalho inteiro é incluído como AAD no AES-GCM — qualquer adulteração nos parâmetros KDF invalida a tag, impedindo downgrade attack
- Novo salt e nonce gerados a cada salvamento — sem reutilização possível
- Pipeline: JSON → `ZLibStream` → AES-GCM → disco

---

## Garantias do sistema

| O que o sistema garante | Como |
|---|---|
| Arquivo capturado → inútil sem a senha | Argon2id + AES-256-GCM |
| Adulteração de qualquer byte → detectada | GCM tag de autenticação |
| Parâmetros KDF protegidos contra downgrade | Cabeçalho como AAD no GCM |
| Nenhum dado sensível em disco fora do `.arcanum` | Vault nunca salvo em aberto |
| Sem reutilização de nonce ou salt | `RandomNumberGenerator` a cada save |
| Arquivo `.arcanum` legível só pelo dono no Linux | `File.SetUnixFileMode(0600)` após cada save |
| Dados descriptografados zerados da heap imediatamente | `CryptographicOperations.ZeroMemory(payload)` após leitura |

---

## Catálogo de Vulnerabilidades e Mitigações

Esta seção documenta as vulnerabilidades mais comuns em gerenciadores de senha e como cada uma é tratada neste projeto. Transparência total — inclusive sobre o que não é resolvido.

---

### V1 — Reutilização de Nonce/IV com a mesma chave

**Consequência:** Em AES-GCM, reutilizar o mesmo nonce com a mesma chave causa comprometimento total da confidencialidade (equivale a um two-time pad — o atacante recupera o XOR dos plaintexts).

**Status: MITIGADO**
Um novo `RandomNumberGenerator.GetBytes(12)` é gerado a cada operação de salvamento. O nonce nunca é reutilizado porque a chave também muda a cada save (novo salt → nova chave via Argon2id).

---

### V2 — KDF fraco ou ausente

**Consequência:** Sem KDF adequado, um arquivo capturado + GPU farm quebra a senha em horas. Usar `SHA256(senha + salt)` direto é equivalente a não ter proteção.

**Status: MITIGADO**
Argon2id com 64 MiB de memória e 3 iterações. Mesmo uma GPU de última geração consegue poucas tentativas por segundo. Uma senha de 12+ caracteres aleatórios é computacionalmente inquebrável nesse cenário.

---

### V3 — Ausência de autenticação do ciphertext

**Consequência:** Sem autenticação, um atacante pode modificar bytes do ciphertext sem detecção (bit-flipping attack), potencialmente corrompendo dados de forma controlada.

**Status: MITIGADO**
AES-GCM é um modo AEAD — autenticação está integrada na cifra. Qualquer modificação no ciphertext invalida a GCM tag. O app rejeita o arquivo antes de expor qualquer byte de dado.

---

### V4 — Senha mestre armazenada em disco ou log

**Consequência:** Trivial — qualquer pessoa com acesso ao disco recupera a senha diretamente.

**Status: MITIGADO**
A senha existe apenas na memória RAM durante a sessão. Nenhum log, nenhum cache, nenhum arquivo temporário. O sistema sequer armazena um hash da senha para verificação — a própria tag GCM serve como prova de autenticidade.

---

### V5 — Comparação de strings não constant-time

**Consequência:** Timing attack — medir o tempo de resposta do sistema revela bytes da senha via análise estatística.

**Status: MITIGADO (por design)**
Não há comparação direta de senha no código — a verificação é feita pela GCM tag, que é constant-time por definição na implementação do .NET (`System.Security.Cryptography.AesGcm`). Para extensões futuras que precisem comparar segredos, usar `CryptographicOperations.FixedTimeEquals()`.

---

### V6 — Dados sensíveis em swap do sistema operacional

**Consequência:** O sistema operacional pode paginar memória RAM para o disco (pagefile). Dados sensíveis como a senha mestre ou o vault decriptografado podem acabar em disco sem o conhecimento do app.

**Status: RISCO RESIDUAL DOCUMENTADO**
A solução completa exigiria `VirtualLock()` via P/Invoke para fixar páginas de memória, impedindo que sejam paginadas. Implementação complexa, fora do escopo do MVP. Risco aceito conscientemente para uso pessoal — a maioria dos sistemas modernos usa pagefile criptografado por padrão no Windows 11.

---

### V7 — Salt estático ou previsível

**Consequência:** Com salt fixo ou derivado deterministicamente, é possível pré-computar rainbow tables e quebrar a senha offline com custo muito menor.

**Status: MITIGADO**
Salt de 32 bytes gerado via `RandomNumberGenerator.GetBytes(32)` a cada salvamento. Nunca hardcoded, nunca derivado da senha ou de timestamp. Completamente aleatório e único por arquivo.

---

### V8 — Metadados vazados no arquivo

**Consequência:** Um atacante com o arquivo pode inferir quantas entradas existem, quando foram criadas, tamanhos de senhas — o que facilita ataques direcionados.

**Status: MITIGADO (parcialmente)**
O payload JSON inteiro — incluindo número de entradas, timestamps e todos os metadados internos — é cifrado. Um atacante vê apenas o tamanho total do arquivo, que revela no máximo a ordem de grandeza do vault (dezenas vs milhares de entradas). Tamanho exato é padronizável em versões futuras.

---

### V9 — Dependência de biblioteca criptográfica não auditada

**Consequência:** Implementações caseiras de criptografia são invariavelmente vulneráveis. Bibliotecas sem auditoria podem conter backdoors ou bugs críticos silenciosos.

**Status: MITIGADO**
Duas dependências criptográficas:
- **`System.Security.Cryptography`** — implementação nativa do .NET, mantida pela Microsoft, parte do runtime auditado e open-source. `AesGcm` e `HKDF` usam as primitivas do sistema operacional (CNG no Windows, OpenSSL no Linux). Zero implementações caseiras de primitivas.
- **`Konscious.Security.Cryptography.Argon2`** — implementação C# do Argon2id, código aberto e auditável. Alternativa: usar P/Invoke direto para a libargon2 se maior garantia for necessária.

Nenhuma outra biblioteca toca dados criptográficos.

---

### V10 — Ausência de proteção de integridade no cabeçalho

**Consequência:** Um atacante que modifique os parâmetros KDF no cabeçalho (ex: forçar `m_cost=1`) pode acelerar drasticamente o brute force sem que o sistema detecte a adulteração.

**Status: MITIGADO**
O cabeçalho completo (magic, version, kdf_id, m_cost, t_cost, p_cost, salt, nonce) é passado como **AAD** (Additional Authenticated Data) para o AES-GCM. Os AAD são autenticados pela GCM tag mas não são cifrados — qualquer alteração nos parâmetros invalida a tag. Esse é um detalhe sutil que a maioria dos projetos similares negligencia.

---

### V11 — Dados sensíveis em memória RAM (strings imutáveis)

**Consequência:** Senhas, seed TOTP e outros segredos ficam em memória como `string` do C# enquanto o app está aberto. C# não permite zerar strings manualmente — o GC decide quando liberar. Em teoria, um atacante com dump de RAM poderia extrair esses valores durante a sessão ativa.

**Status: RISCO RESIDUAL — LIMITAÇÃO PARCIALMENTE MITIGADA**
Essa limitação existe em qualquer linguagem com strings imutáveis gerenciadas por GC (Python, JavaScript, Java, C#). O Arcanum mitiga o que é possível:
- A chave de encriptação (`encKey`) é zerada via `ZeroMemory()` imediatamente após cada operação criptográfica
- O array `payload` (JSON comprimido) é zerado via `ZeroMemory()` imediatamente após a leitura na descriptografia
- A janela de exposição desses dados na memória é mínima

Senhas e seeds precisam permanecer em memória durante toda a sessão (necessário para salvar alterações) — isso não tem como evitar com `string`. Uma mitigação futura seria usar `byte[]` + `CryptographicOperations.ZeroMemory()` para campos sensíveis internos, em vez de `string`.

Na prática, o vetor real de ataque é o **arquivo em disco** — e esse é protegido por AES-256-GCM. Explorar RAM exige acesso privilegiado ao processo em execução; se o atacante tem isso, o problema é o sistema operacional, não o gerenciador de senhas.

---

## Resumo do catálogo

| # | Vulnerabilidade | Status |
|---|---|---|
| V1 | Nonce/IV reutilizado | Mitigado |
| V2 | KDF fraco ou ausente | Mitigado |
| V3 | Sem autenticação do ciphertext | Mitigado |
| V4 | Senha em disco ou log | Mitigado |
| V5 | Comparação não constant-time | Mitigado por design |
| V6 | Dados sensíveis em swap do OS | Risco residual documentado |
| V7 | Salt estático ou previsível | Mitigado |
| V8 | Metadados vazados | Mitigado (parcialmente) |
| V9 | Biblioteca não auditada | Mitigado |
| V10 | Cabeçalho sem integridade | Mitigado |
| V11 | Strings em RAM não zeráveis | Risco residual — mitigado parcialmente via ZeroMemory |
