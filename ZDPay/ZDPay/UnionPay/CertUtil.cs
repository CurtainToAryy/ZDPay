﻿using System.Collections.Generic;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkix;
using Org.BouncyCastle.X509.Store;
using System.Collections;
using Org.BouncyCastle.Utilities.Collections;
using System.IO;
using System.Text;
using ZDPay.Log;
using System;
using System.Reflection;

namespace ZDPay.UnionPay
{
    public class Cert
    {
        public AsymmetricKeyParameter key;
        public X509Certificate cert;
        public string certId;
    }

    public class CertUtil
    {
        static CertUtil()
        {
            initCerCerts();
            initEncryptCert();
            initMiddleCert();
            initRootCert();
            initSignCert(SDKConfig.SignCertPath, SDKConfig.SignCertPwd);
        }
        /// <summary>
        /// 日志名称
        /// </summary>
        private readonly static LogHelp log = new LogHelp("UnionPay");
        //签名证书，key是路径
        private static Dictionary<string, Cert> signCerts = new Dictionary<string, Cert>();
        //5.0.0验签证书，key是certId
        private static Dictionary<string, Cert> cerCerts = new Dictionary<string, Cert>();
        //加密证书
        private static Cert encryptCert = null;
        //5.1.0验签证书的根证书
        private static X509Certificate rootCert = null;
        //5.1.0验签证书的中级证书
        private static X509Certificate middleCert = null;
        //5.1.0验签证书，key是应答的证书的base64
        private static Dictionary<string, X509Certificate> validateCerts = new Dictionary<string, X509Certificate>();

        private static readonly string UNIONPAY_CNNAME = "中国银联股份有限公司";

        private static void initSignCert(string certPath, string certPwd)
        {

            log.info("读取签名证书……");

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(certPath, FileMode.Open);

                Pkcs12Store store = new Pkcs12Store(fileStream, certPwd.ToCharArray());

                string pName = null;
                foreach (string n in store.Aliases)
                {
                    if (store.IsKeyEntry(n))
                    {
                        pName = n;
                        //break;
                    }
                }

                Cert signCert = new Cert();
                signCert.key = store.GetKey(pName).Key;
                X509CertificateEntry[] chain = store.GetCertificateChain(pName);
                signCert.cert = chain[0].Certificate;
                signCert.certId = signCert.cert.SerialNumber.ToString();

                signCerts[certPath] = signCert;
                log.info("签名证书读取成功，序列号：" + signCert.certId);

            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
            }

        }


        /// <summary>
        /// 获取签名证书私钥
        /// </summary>
        /// <returns></returns>
        public static AsymmetricKeyParameter GetSignKeyFromPfx()
        {
            log.info("取配置文件证书");
            return GetSignKeyFromPfx(SDKConfig.SignCertPath, SDKConfig.SignCertPwd);
        }

        /// <summary>
        /// 获取签名证书私钥
        /// </summary>
        /// <returns></returns>
        public static AsymmetricKeyParameter GetSignKeyFromPfx(string certPath, string certPwd)
        {
            log.info("传入证书路径获取私钥。");
            if (!signCerts.ContainsKey(certPath))
            {
                initSignCert(certPath, certPwd);
            }
            return signCerts[certPath].key;
        }


        /// <summary>
        /// 获取签名证书的证书序列号
        /// </summary>
        /// <returns></returns>
        public static string GetSignCertId(string certPath, string certPwd)
        {
            log.info("传入证书路径获取certId。");
            if (!signCerts.ContainsKey(certPath))
            {
                initSignCert(certPath, certPwd);
            }
            return signCerts[certPath].certId;
        }

        /// <summary>
        /// 获取签名证书的证书序列号
        /// </summary>
        /// <returns></returns>
        public static string GetSignCertId()
        {
            log.info("取配置文件证书");
            return GetSignCertId(SDKConfig.SignCertPath, SDKConfig.SignCertPwd);
        }

        private static void initEncryptCert()
        {
            if (SDKConfig.EncryptCert == null)
            {
                log.info("未配置加密证书路径，不做初始化。");
                return;
            }
            log.info("读取加密证书……");

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(SDKConfig.EncryptCert, FileMode.Open);
                X509Certificate cert = new X509CertificateParser().ReadCertificate(fileStream);

                encryptCert = new Cert();
                encryptCert.cert = cert;
                encryptCert.certId = cert.SerialNumber.ToString();
                encryptCert.key = cert.GetPublicKey();

                log.info("加密证书读取成功，序列号：" + encryptCert.certId);
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
            }
        }


        private static void initMiddleCert()
        {
            if (SDKConfig.MiddleCertPath == null)
            {
                log.info("未配置中级证书路径，不做初始化。");
                return;
            }
            log.info("读取中级证书……");

            if (SDKConfig.MiddleCertPath == null || !File.Exists(SDKConfig.MiddleCertPath))
            {
                log.error("middleCertPath为空或路径不存在，请检查配置文件middleCertPath的配置情况。");
                return;
            }
            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(SDKConfig.MiddleCertPath, FileMode.Open);
                middleCert = new X509CertificateParser().ReadCertificate(fileStream);
                log.info("中级证书读取成功。");
            }
            catch (Exception e)
            {
                log.error("中级证书读取失败：" + e.Message);
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
            }
        }


        private static void initRootCert()
        {
            if (SDKConfig.RootCertPath == null)
            {
                log.info("未配置根证书路径，不做初始化。");
                return;
            }
            log.info("读取根证书……");

            if (SDKConfig.RootCertPath == null || !File.Exists(SDKConfig.RootCertPath))
            {
                log.error("rootCertPath为空或路径不存在，请检查配置文件rootCertPath的配置情况。");
                return;
            }
            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(SDKConfig.RootCertPath, FileMode.Open);
                rootCert = new X509CertificateParser().ReadCertificate(fileStream);
                log.info("根证书读取成功。");
            }
            catch (Exception e)
            {
                log.error("根证书读取失败："+e.Message);
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Close();
            }
        }


        public static X509Certificate VerifyAndGetPubKey(string signPubKeyCert)
        {
            if (!validateCerts.ContainsKey(signPubKeyCert))
            {
                log.info("开始处理signPubKeyCert。");
                X509Certificate x509Cert = GetPubKeyCert(signPubKeyCert);
                if (x509Cert == null)
                {
                    return null;
                }
                if (VerifyCertificate(x509Cert))
                {
                    validateCerts.Add(signPubKeyCert, x509Cert);
                }
                else
                {
                    log.error("验证验签证书失败。");
                    return null;
                }
            }
            return validateCerts[signPubKeyCert];
        }

        public static X509Certificate GetPubKeyCert(string pubKeyCert)
        {
            try
            {
                pubKeyCert = pubKeyCert.Replace("-----END CERTIFICATE-----", "").Replace("-----BEGIN CERTIFICATE-----", "");
                byte[] x509CertBytes = Convert.FromBase64String(pubKeyCert);
                X509CertificateParser cf = new X509CertificateParser();
                X509Certificate x509Cert = cf.ReadCertificate(x509CertBytes);
                return x509Cert;
            }
            catch (Exception e)
            {
                log.error("convert pubKeyCert failed." + e.Message);
                return null;
            }
        }

        private static Boolean VerifyCertificateChain(X509Certificate cert)
        {
            if (null == cert)
            {
                log.error("cert must Not null");
                return false;
            }
            X509Certificate rootCert = GetRootCert();
            if (null == rootCert)
            {
                log.error("rootCert must Not null");
                return false;
            }
            X509Certificate middleCert = GetMiddleCert();
            if (null == middleCert)
            {
                log.error("middleCert must Not null");
                return false;
            }

            try
            {

                X509CertStoreSelector selector = new X509CertStoreSelector();
                selector.Subject = cert.SubjectDN;

                ISet trustAnchors = new HashSet();
                trustAnchors.Add(new TrustAnchor(rootCert, null));
                PkixBuilderParameters pkixParams = new PkixBuilderParameters(
                        trustAnchors, selector);

                IList intermediateCerts = new ArrayList();
                intermediateCerts.Add(rootCert);
                intermediateCerts.Add(middleCert);
                intermediateCerts.Add(cert);

                pkixParams.IsRevocationEnabled = false;

                IX509Store intermediateCertStore = X509StoreFactory.Create(
                    "Certificate/Collection",
                    new X509CollectionStoreParameters(intermediateCerts));
                pkixParams.AddStore(intermediateCertStore);

                PkixCertPathBuilder pathBuilder = new PkixCertPathBuilder();
                PkixCertPathBuilderResult result = pathBuilder.Build(pkixParams);
                PkixCertPath path = result.CertPath;
                log.info("verify certificate chain succeed.");
                return true;
            }
            catch (PkixCertPathBuilderException e)
            {
                log.error("verify certificate chain fail." + e.Message);
            }
            catch (Exception e)
            {
                log.error("verify certificate chain exception: " + e.Message);
            }
            return false;
        }

        private static Boolean VerifyCertificate(X509Certificate x509Cert)
        {
            string cn = GetIdentitiesFromCertficate(x509Cert);
            try
            {
                x509Cert.CheckValidity();//验证有效期
                                         // x509Cert.Verify(rootCert.GetPublicKey());
                if (!VerifyCertificateChain(x509Cert))  //验证书链
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                log.error("verifyCertificate fail" + e.Message);
                return false;
            }

            if (!"false".Equals(SDKConfig.IfValidateCNName))
            {
                // 验证公钥是否属于银联
                if (!UNIONPAY_CNNAME.Equals(cn))
                {
                    log.error("cer owner is not CUP:" + cn);
                    return false;
                }

            }
            else
            {
                if (!UNIONPAY_CNNAME.Equals(cn) && !"00040000:SIGN".Equals(cn))
                {
                    log.error("cer owner is not CUP:" + cn);
                    return false;
                }
            }
            return true;

        }


        private static string GetIdentitiesFromCertficate(X509Certificate aCert)
        {
            string tDN = aCert.SubjectDN.ToString();
            string tPart = "";
            if ((tDN != null))
            {
                string[] tSplitStr = tDN.Substring(tDN.IndexOf("CN=")).Split("@".ToCharArray());
                if (tSplitStr != null && tSplitStr.Length > 2
                        && tSplitStr[2] != null)
                    tPart = tSplitStr[2];
            }
            return tPart;
        }


        /// <summary>
        /// 获取加密证书的证书序列号
        /// </summary>
        /// <returns></returns>
        public static string GetEncryptCertId()
        {
            if (encryptCert == null)
            {
                initEncryptCert();
            }
            return encryptCert.certId;
        }


        /// <summary>
        /// 获取加密证书的证书序列号
        /// </summary>
        /// <returns></returns>
        public static X509Certificate GetRootCert()
        {
            if (rootCert == null)
            {
                initRootCert();
            }
            return rootCert;
        }


        /// <summary>
        /// 获取加密证书的证书序列号
        /// </summary>
        /// <returns></returns>
        public static X509Certificate GetMiddleCert()
        {
            if (middleCert == null)
            {
                initMiddleCert();
            }
            return middleCert;
        }

        /// <summary>
        /// 获取加密证书的RSACryptoServiceProvider
        /// </summary>
        /// <returns></returns>
        public static AsymmetricKeyParameter GetEncryptKey()
        {
            if (encryptCert == null)
            {
                initEncryptCert();
            }
            return encryptCert.key;
        }

        private static void initCerCerts()
        {
            if (SDKConfig.ValidateCertDir == null)
            {
                log.info("未配置验签证书路径，不做初始化。");
                return;
            }
            log.info("读取验签证书文件夹下所有cer文件……");
            DirectoryInfo directory = new DirectoryInfo(SDKConfig.ValidateCertDir);
            FileInfo[] files = directory.GetFiles("*.cer");
            if (null == files || 0 == files.Length)
            {
                log.info("请确定[" + SDKConfig.ValidateCertDir + "]路径下是否存在cer文件");
                return;
            }
            foreach (FileInfo file in files)
            {
                FileStream fileStream = null;
                try
                {
                    fileStream = new FileStream(file.DirectoryName + "\\" + file.Name, FileMode.Open);
                    X509Certificate certificate = new X509CertificateParser().ReadCertificate(fileStream);

                    Cert cert = new Cert();
                    cert.cert = certificate;
                    cert.certId = certificate.SerialNumber.ToString();
                    cert.key = certificate.GetPublicKey();
                    cerCerts[cert.certId] = cert;
                    log.info(file.Name + "读取成功，序列号：" + cert.certId);
                }
                finally
                {
                    if (fileStream != null)
                        fileStream.Close();
                }
            }
        }

        /// <summary>
        /// 通过证书id，获取验证签名的证书
        /// </summary>
        /// <param name="certId"></param>
        /// <returns></returns>
        public static AsymmetricKeyParameter GetValidateKeyFromPath(string certId)
        {
            if (cerCerts == null || cerCerts.Count <= 0)
            {
                initCerCerts();
            }
            if (cerCerts == null || cerCerts.Count <= 0)
            {
                log.info("未读取到任何证书……");
                return null;
            }
            if (cerCerts.ContainsKey(certId))
            {
                return cerCerts[certId].key;
            }
            else
            {
                log.info("未匹配到序列号为[" + certId + "]的证书");
                return null;
            }
        }
        public static void resetEncryptCertPublicKey()
        {
            encryptCert = null;
            initEncryptCert();
        }

    }
}
