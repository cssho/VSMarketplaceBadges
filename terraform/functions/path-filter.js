// CloudFront Functions (viewer-request) — アプリが応答しうる形のパス以外をエッジで 404 にする。
//
// 背景: 2026-10-07 04:05 JST、単一 IP から /config.zip のような推測パスが 2 秒で 3,226 件届いた。
// URL がすべて異なるのでキャッシュに当たらず全件 Lambda に抜け、同時実行数がアカウント上限の
// 1000 に張り付いて 1,980 件がスロットルされた。ここで弾けば Lambda には届かない。
//
// ★ 拡張子では絞らないこと。ImageExt に無い拡張子 (.jpg など) や拡張子なしのパスも、アプリは
//   既定の SVG を 200 で返しており、実際に README で使われている (1 日 1 万件超)。
//   判定は 1 段目のバッジタイプだけで行う。
//
// ★ BADGE_TYPES は Entity/BadgeType.cs の [EnumMember] と、STATIC_PATHS は wwwroot/ の中身と
//   一致させる。漏れると該当バッジ・ファイルが本番で 404 になる。
//   CloudFrontPathFilterTests が両方を突き合わせるので、変更後は dotnet test を実行すること。

var BADGE_TYPES = [
  'version',
  'version-short',
  'installs',
  'installs-short',
  'rating',
  'rating-short',
  'rating-star',
  'trending-daily',
  'trending-weekly',
  'trending-monthly',
  'downloads',
  'downloads-short'
];

var STATIC_PATHS = [
  '/',
  '/index.html',
  '/favicon.ico',
  '/image/vsmb.png',
  '/unavailable.png',
  '/unavailable.svg'
];

function handler(event) {
  var request = event.request;
  var uri = request.uri;

  if (STATIC_PATHS.indexOf(uri) !== -1) {
    return request;
  }

  // "/version/foo.bar.svg" → ["", "version", "foo.bar.svg"]
  // 末尾スラッシュ付きなど細部の解釈はアプリ側に任せ、ここでは 1 段目しか見ない。
  var segments = uri.split('/');
  if (segments.length >= 3 && segments[2] !== '' && BADGE_TYPES.indexOf(segments[1]) !== -1) {
    return request;
  }

  return {
    statusCode: 404,
    statusDescription: 'Not Found'
  };
}
