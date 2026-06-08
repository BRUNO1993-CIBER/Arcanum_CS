# Arcanum — Como o sistema funciona por dentro

Esta seção explica o que acontece nos bastidores — não é preciso saber programar para entender.

---

## O arquivo `.arcanum` — o único arquivo que importa

O Arcanum salva **um único arquivo** no seu computador. Esse arquivo contém todas as suas senhas, mas completamente embaralhadas — sem a senha mestre, é lixo digital. Pode estar na nuvem, num pendrive, ou enviado por e-mail: sem a senha, ninguém abre.

### Por que a extensão `.arcanum`?

`.arcanum` não é um formato reconhecido pelo sistema operacional — é uma extensão proprietária criada exclusivamente para este app. O nome vem diretamente do aplicativo (ARCANUM).

Isso é intencional por três razões:

- **Não abre no programa errado por acidente** — Windows não associa `.arcanum` a nenhum editor de texto ou outro software
- **Identifica visualmente o arquivo** — ao ver um `.arcanum` em qualquer pasta ou pendrive, você sabe imediatamente o que é, sem precisar abrir
- **Convenção dos gerenciadores de senha** — é a mesma abordagem de KeePass (`.kdbx`), 1Password (`.1pif`) e Bitwarden (`.json` com estrutura própria): formato proprietário vinculado ao app

Internamente, o arquivo é um **binário criptografado** — não é texto, não é ZIP, não é banco de dados. A estrutura começa com o magic `PVLT` nos primeiros 4 bytes (identificação binária do formato), seguido do cabeçalho com parâmetros KDF, e termina com o ciphertext AES-GCM. A extensão e o magic são coisas diferentes: a extensão identifica o arquivo no sistema de arquivos; o magic identifica o formato dentro do arquivo.

---

## Por que o app precisa da senha mestre para editar entradas?

Quando você clica em `Aplicar`, o app não "acrescenta" a entrada no arquivo. Ele faz isso:

```
1. Pega TODAS as suas entradas (antigas + nova)
2. Converte tudo para texto JSON
3. Comprime esse texto (zlib)
4. Criptografa TUDO do zero, com chave nova
5. Substitui o arquivo antigo pelo novo de forma atômica
```

O arquivo inteiro é **re-criptografado a cada salvamento**, com um novo salt e nonce aleatório. Por isso a senha mestre precisa estar disponível durante toda a sessão — ela fica na memória RAM e é descartada quando o app fecha. Nunca toca o disco.

---

## O que acontece quando você digita a senha mestre

```
Sua senha mestre
      │
      ▼
  Argon2id ◄── salt aleatório (32 bytes novos a cada save)
      │         (leva ~0.5s — torna brute force computacionalmente inviável)
      ▼
  Chave mestra de 256 bits
      │
      ▼
   HKDF-SHA256 ◄── isola e expande para a chave de encriptação
      │
      ▼
  AES-256-GCM ◄── criptografa os dados + gera assinatura de autenticidade
      │
      ▼
  Arquivo .arcanum gravado no disco
```

**Argon2id** é deliberadamente lento — ~0.5 segundo no seu hardware. Para você, imperceptível. Para um atacante com uma GPU testando bilhões de senhas, cada tentativa ainda leva 0.5 segundo: torna o brute force inviável na prática.

**AES-256-GCM** faz duas coisas simultaneamente: criptografa os dados E gera uma assinatura matemática (tag). Se qualquer byte do arquivo for alterado — um único bit — a assinatura não bate e o app rejeita o arquivo antes de expor qualquer dado.

---

## O que acontece quando você abre o app

```
Arquivo .arcanum
      │
      ▼
  Lê o cabeçalho (salt, parâmetros KDF) — não cifrado, mas autenticado
      │
      ▼
  Você digita a senha mestre
      │
      ▼
  Argon2id reconstrói a chave usando o salt do arquivo
      │
      ▼
  AES-256-GCM tenta descriptografar + verifica a assinatura
      │
      ├── Assinatura OK → descomprime o JSON → carrega suas entradas
      │
      └── Assinatura falhou → "Senha incorreta ou vault adulterado"
          (intencionalmente ambíguo — não vazar qual dos dois ocorreu)
```

---

## Resumo visual

```
┌──────────────────────────────────────────────────────┐
│                    Seu computador                    │
│                                                      │
│  ┌──────────┐   senha mestre   ┌──────────────────┐  │
│  │  Arcanum  │ ───────────────► │   Memória (RAM)  │  │
│  │  (app)   │                  │  • senha mestre  │  │
│  └──────────┘                  │  • dados abertos │  │
│       │                        └──────────────────┘  │
│       │  salva (sempre criptografado)                │
│       ▼                                              │
│  ┌─────────────────────────────────┐                 │
│  │   ~/.arcanum/vault.arcanum       │                 │
│  │   (ilegível sem a senha mestre) │                 │
│  └─────────────────────────────────┘                 │
└──────────────────────────────────────────────────────┘
```

---

## É possível burlar o sistema?

**Não.** E entender o porquê é o ponto central da arquitetura.

### A proteção não está no código — está na matemática

A interface gráfica (Avalonia) é apenas conveniência. A segurança está em `Core/Crypto.cs` e não pode ser contornada por modificação de código:

**Tentativa: rodar o app sem digitar a senha**
Mesmo removendo toda a UI, ainda é preciso chamar `Crypto.DecryptVault()`. Sem a senha certa, o Argon2id gera uma chave errada. O `AesGcm.Decrypt` lança `CryptographicException`. Esse erro vem da implementação nativa do .NET — não há `if` C# que impeça isso.

**Tentativa: remover os `throw` de erro do código**
Pode deletar todos os tratamentos de erro. O resultado é bytes de ruído aleatório — dados cifrados com chave errada não produzem JSON, produzem lixo matemático.

**Tentativa: ler o arquivo `.arcanum` diretamente**
Após os 71 bytes de cabeçalho, tudo é ciphertext — matematicamente indistinguível de números aleatórios. Não há texto, não há estrutura visível.

### O que realmente poderia funcionar

| Ataque | Funciona? | O que exige |
|---|---|---|
| Brute force da senha mestre | Teoricamente | Bilhões de anos com senha forte + Argon2id |
| Keylogger capturando a senha ao digitar | Sim | Acesso ao computador *antes* de você rodar o app |
| Dump de memória RAM com o app aberto | Sim | Acesso administrativo ao computador *em tempo real* |
| Substituir o `.exe` por versão maliciosa | Sim | Acesso ao computador *antes* de você rodar o app |

O padrão é sempre o mesmo: **qualquer ataque real exige comprometer o computador antes ou durante o uso.** Nesse ponto o problema deixou de ser o gerenciador de senhas — é o sistema operacional inteiro que está comprometido.

**Conclusão:** o `.arcanum` pode ser capturado, copiado, publicado — não importa. É inútil sem a senha mestre. A superfície de ataque real é o dispositivo onde o app roda, não o app em si.

**PCs compartilhados:** a solução correta é cada pessoa ter sua própria conta no sistema operacional. Cada conta tem seu próprio `~/.arcanum/vault.arcanum` isolado — o outro usuário não consegue acessar sem privilégio de admin. O Arcanum já suporta isso nativamente.

---

## Como o gerador de senhas funciona — dá para prever?

**Não.** A razão é a diferença entre dois tipos de aleatoriedade no .NET:

| Classe | Tipo | Previsível? | Usado para |
|---|---|---|---|
| `System.Random` | Pseudoaleatório | **Sim** — seed baseado no tempo | Jogos, simulações |
| `RandomNumberGenerator` | CSPRNG do sistema operacional | **Não** | Criptografia, senhas |

O Arcanum usa exclusivamente `RandomNumberGenerator`, que é uma interface direta para o CSPRNG do Windows (BCryptGenRandom) — o mesmo usado para gerar os salts e nonces do vault.

### De onde vem a aleatoriedade de verdade?

O sistema operacional mantém um **pool de entropia** alimentado continuamente por eventos físicos imprevisíveis:

```
Fontes de entropia do OS
        │
        ├── Timing de interrupções de hardware (microssegundos)
        ├── Movimentos e cliques do mouse
        ├── Timing de teclas pressionadas
        ├── Tráfego de rede (latências)
        ├── Leitura/escrita em disco
        └── Ruído térmico de sensores (em hardware moderno)
        │
        ▼
   Pool de entropia do OS (BCryptGenRandom no Windows / /dev/urandom no Linux)
        │
        ▼
   RandomNumberGenerator.GetBytes() / RandomNumberGenerator.Fill()
        │
        ▼
   Cada caractere da senha gerada
```

Isso significa que a senha gerada **não tem relação com o horário, com o seu nome, com dados do sistema, ou com qualquer outra entrada previsível**. Dois cliques em "Gerar" na mesma milissegundo produzem senhas completamente diferentes.

### O que o gerador produz

```
Comprimento padrão:    24 caracteres
Charset:               a-z + A-Z + 0-9 + !@#$%^&*()-_=+
```

Exemplo de saída: `K#9mPx!2nQr&Lv$5Yw@Jb*8` — 24 caracteres, ~142 bits de entropia. Um ataque de força bruta contra essa senha, mesmo com um supercomputador, levaria mais tempo do que a idade estimada do universo.
