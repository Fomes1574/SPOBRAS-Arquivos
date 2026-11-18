# Gera a fonte do relatorio (SpobrasRelatorio-*.ttf) a partir da Carlito original (SIL OFL 1.1):
# so as letras que o relatorio aceita (TextoMedido.TemDesenho), sem hinting e com outro nome, como a licenca
# pede para versao modificada. Uso: python3 reduzir-fonte.py <pasta com Carlito-Regular.ttf e Carlito-Bold.ttf>
# Copie os dois arquivos gerados para esta pasta e para src/Spobras.Celular.Telas/wwwroot/fontes.
import sys
from fontTools.ttLib import TTFont
from fontTools import subset
extras = "–—‘’“”…•€ŒœŠšŽžŸƒ"
chars = [chr(c) for c in range(0x20, 0x7F)] + [chr(c) for c in range(0xA0, 0x100)] + list(extras)
for origem, destino, estilo in [(sys.argv[1]+'/Carlito-Regular.ttf', 'SpobrasRelatorio-Regular.ttf', 'Regular'), (sys.argv[1]+'/Carlito-Bold.ttf', 'SpobrasRelatorio-Bold.ttf', 'Bold')]:
    f = TTFont(origem)
    op = subset.Options(); op.layout_features = ['*']; op.name_IDs = ['*']; op.name_languages = ['*']; op.hinting = False; op.notdef_outline = True
    s = subset.Subsetter(op); s.populate(unicodes=[ord(c) for c in chars]); s.subset(f)
    nome = f['name']
    for rec in list(nome.names):
        if rec.nameID in (1, 16): nome.setName('SPOBRAS Relatorio', rec.nameID, rec.platformID, rec.platEncID, rec.langID)
        elif rec.nameID == 4: nome.setName('SPOBRAS Relatorio ' + estilo, rec.nameID, rec.platformID, rec.platEncID, rec.langID)
        elif rec.nameID == 6: nome.setName('SPOBRASRelatorio-' + estilo, rec.nameID, rec.platformID, rec.platEncID, rec.langID)
        elif rec.nameID == 3: nome.setName('SPOBRAS Relatorio ' + estilo + ' (subconjunto de Carlito)', rec.nameID, rec.platformID, rec.platEncID, rec.langID)
    f.save(destino)
    g = TTFont(destino)
    print(destino, len(g.getGlyphOrder()), 'glifos', [r.toUnicode() for r in g['name'].names if r.nameID in (1,4,6)][:3])
    # larguras iguais
    a = TTFont(origem); cm_a = a.getBestCmap(); cm_g = g.getBestCmap()
    dif = [c for c in chars if ord(c) in cm_a and a['hmtx'][cm_a[ord(c)]][0] != g['hmtx'][cm_g[ord(c)]][0]]
    print('  diferencas de largura:', dif, 'sem glifo no original:', [hex(ord(c)) for c in chars if ord(c) not in cm_a], 'faltando no novo:', [hex(ord(c)) for c in chars if ord(c) in cm_a and ord(c) not in cm_g])
