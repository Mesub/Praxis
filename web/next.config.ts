import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Dev proxy: the browser talks same-origin to Next, which forwards /api to the .NET core —
  // no CORS surface on the API. Production puts both behind one origin anyway.
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${process.env.PRAXIS_API_URL ?? "http://localhost:5175"}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
