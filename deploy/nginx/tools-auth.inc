# HTTP basic auth in front of the ops tools, none of which is safe to expose
# without one (Kafka UI, Prometheus and Seq have no login of their own here).
# The file is written at container start from TOOLS_USER / TOOLS_PASSWORD
# (40-medguard-htpasswd.sh); without a password it is empty and nobody gets in.
auth_basic "MedGuard ops";
auth_basic_user_file /etc/nginx/medguard/htpasswd;

# The tools never need the visitor's credentials.
proxy_set_header Authorization "";
