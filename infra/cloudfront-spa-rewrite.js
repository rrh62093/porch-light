// CloudFront Function (viewer request) for the DEFAULT (S3) behavior only.
// Sends app routes like /deals/123 to index.html so the PWA router handles them,
// without touching /api/* (a separate behavior) or real files (anything with a dot).
function handler(event) {
  var req = event.request;
  if (req.uri.indexOf('.') === -1) req.uri = '/index.html';
  return req;
}
